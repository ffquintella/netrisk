using DAL;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Assessments;
using Model.Exceptions;
using Model.File;
using ServerServices.Interfaces;
using Tools.Security;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

/// <summary>
/// A comment and evidence files on each answer of an assessment run (GitHub #80, T297, S44).
///
/// The content of an upload goes through <see cref="IFilesService"/> — the same chunk staging,
/// unguessable unique name and entity stamping every other attachment gets — and what this service adds
/// is what the generic files routes cannot know: that the run is still open, that the question belongs
/// to it, the per-answer limit and the evidence size cap (S44 D5, D6).
///
/// Logs carry ids only. A comment or a file name can carry personal data.
/// </summary>
public class AssessmentRunEvidenceService(ILogger logger, IDalService dalService, IFilesService filesService)
    : ServiceBase(logger, dalService), IAssessmentRunEvidenceService
{
    /// <summary>The rule a write to a submitted run breaks; answered 409 by the API.</summary>
    public const string RunSubmittedRule = "run_submitted";

    /// <summary>The rule an eleventh file on one answer breaks; answered 409 by the API.</summary>
    public const string EvidenceLimitRule = "evidence_limit_reached";

    public async Task<AssessmentRunAnswer> SaveCommentAsync(int runId, int questionId, string? comment)
    {
        var normalized = AssessmentEvidencePolicy.NormalizeComment(comment);
        if (!AssessmentEvidencePolicy.IsCommentWithinLimit(normalized))
            throw new InvalidParameterException("comment",
                $"The comment may be at most {AssessmentEvidencePolicy.MaxCommentLength} characters.");

        await using var db = DalService.GetContext();

        var run = await LoadOpenRunAsync(db, runId);
        await EnsureQuestionOfRunAsync(db, run, questionId);

        var answer = await GetOrCreateAnswerAsync(db, runId, questionId);
        answer.Comment = normalized;
        answer.LastUpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Logger.Information("Comment {Action} on assessment run {RunId}, question {QuestionId}",
            normalized is null ? "cleared" : "saved", runId, questionId);

        // A detached copy: the tracked row has had the run fixed up onto it, and the caller asked for
        // the answer, not for the run behind it.
        return new AssessmentRunAnswer
        {
            Id = answer.Id,
            AssessmentRunId = answer.AssessmentRunId,
            AssessmentQuestionId = answer.AssessmentQuestionId,
            AnswerContentJson = answer.AnswerContentJson,
            Comment = answer.Comment,
            LastUpdatedAt = answer.LastUpdatedAt
        };
    }

    public async Task<List<AssessmentAnswerEvidence>> GetRunEvidenceAsync(int runId)
    {
        await using var db = DalService.GetContext();

        if (!await db.AssessmentRuns.AnyAsync(r => r.Id == runId))
            throw new DataNotFoundException("assessment_runs", runId.ToString());

        var typeNames = await FileTypeNamesAsync(db);

        // Projected, so the content column never leaves the database for a listing.
        var rows = await (from file in db.NrFiles
                join answer in db.AssessmentRunAnswers on file.AssessmentRunAnswerId equals (int?)answer.Id
                where answer.AssessmentRunId == runId
                orderby file.Timestamp, file.Id
                select new
                {
                    file.Name, file.UniqueName, file.Type, file.Timestamp, file.User, file.Size,
                    AnswerId = answer.Id, answer.AssessmentQuestionId
                })
            .ToListAsync();

        return rows.Select(r => new AssessmentAnswerEvidence
        {
            Name = r.Name,
            UniqueName = r.UniqueName,
            Type = r.Type is not null && typeNames.TryGetValue(r.Type, out var typeName) ? typeName : r.Type,
            Timestamp = r.Timestamp,
            OwnerId = r.User,
            Size = r.Size,
            AssessmentRunAnswerId = r.AnswerId,
            QuestionId = r.AssessmentQuestionId
        }).ToList();
    }

    public async Task<AssessmentAnswerEvidence> AttachEvidenceAsync(int runId, int questionId,
        AssessmentEvidenceUploadRequest request, User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (request is null) throw new InvalidParameterException("request", "The upload description is missing.");

        // Everything that can be judged without the database first, so a malformed request costs nothing.
        var name = AssessmentEvidencePolicy.NormalizeFileName(request.Name)
                   ?? throw new InvalidParameterException("name", "The file name is empty.");

        if (!SafePathTool.IsSafeSegment(request.FileId))
            throw new InvalidParameterException("fileId",
                "The upload id must be a single path segment of letters, digits, dashes or underscores.");

        if (request.TotalChunks < 1)
            throw new InvalidParameterException("totalChunks", "At least one chunk must have been staged.");

        var allowedTypes = filesService.GetFileTypes();
        if (string.IsNullOrWhiteSpace(request.Type) ||
            !allowedTypes.Any(t => string.Equals(t.Value.ToString(), request.Type, StringComparison.Ordinal)))
            throw new InvalidParameterException("type", "The file type is not one the server accepts.");

        int answerId;
        await using (var db = DalService.GetContext())
        {
            var run = await LoadOpenRunAsync(db, runId);
            await EnsureQuestionOfRunAsync(db, run, questionId);

            // Saved now — a no-op when the row already existed — because the file needs its real id.
            var answer = await GetOrCreateAnswerAsync(db, runId, questionId);
            await db.SaveChangesAsync();
            answerId = answer.Id;

            var attached = await db.NrFiles.CountAsync(f => f.AssessmentRunAnswerId == answerId);
            if (attached >= AssessmentEvidencePolicy.MaxEvidencePerAnswer)
                throw new RuleBrokenException(
                    $"An answer may carry at most {AssessmentEvidencePolicy.MaxEvidencePerAnswer} evidence files.",
                    EvidenceLimitRule);
        }

        // Metadata only: the content is the staged chunks, and the size, unique name, uploader,
        // timestamp and entity are all set by the files service, never taken from the client.
        var file = new NrFile
        {
            Name = name,
            Type = request.Type,
            ViewType = (int)FileCollectionType.AssessmentRunAnswerFile,
            AssessmentRunAnswerId = answerId,
            UniqueName = string.Empty,
            Content = [],
            Size = 0
        };

        var listing = filesService.CompleteChunkedUpload(file, request.FileId, request.TotalChunks, user,
            AssessmentEvidencePolicy.MaxEvidenceBytes);

        Logger.Information(
            "User {User} attached evidence ({Size} bytes) to assessment run {RunId}, question {QuestionId}",
            user.Value, file.Size, runId, questionId);

        return new AssessmentAnswerEvidence
        {
            Name = listing.Name,
            UniqueName = listing.UniqueName,
            Type = listing.Type,
            Timestamp = listing.Timestamp,
            OwnerId = listing.OwnerId,
            Size = file.Size,
            AssessmentRunAnswerId = answerId,
            QuestionId = questionId
        };
    }

    public async Task DeleteEvidenceAsync(int runId, int questionId, string uniqueName, User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var db = DalService.GetContext();

        await LoadOpenRunAsync(db, runId);

        // Matched on the run *and* the question, so this route cannot reach any other file — not even
        // another answer's evidence in the same run.
        var target = await (from file in db.NrFiles
                join answer in db.AssessmentRunAnswers on file.AssessmentRunAnswerId equals (int?)answer.Id
                where file.UniqueName == uniqueName
                      && answer.AssessmentRunId == runId
                      && answer.AssessmentQuestionId == questionId
                select new { file.Id, file.User })
            .FirstOrDefaultAsync()
            ?? throw new DataNotFoundException("assessment_evidence", $"{runId}/{questionId}");

        if (!user.Admin && target.User != user.Value)
            throw new PermissionInvalidException("evidence_owner", user.Value, "delete assessment evidence");

        // Removed by key, so a 20 MiB blob is not read only to be deleted.
        db.NrFiles.Remove(new NrFile { Id = target.Id, Name = string.Empty, UniqueName = string.Empty, Content = [] });
        await db.SaveChangesAsync();

        Logger.Information("User {User} deleted evidence file {FileId} from assessment run {RunId}, question {QuestionId}",
            user.Value, target.Id, runId, questionId);
    }

    /// <summary>The run, through the caller's scope; refused when it no longer accepts changes.</summary>
    private static async Task<AssessmentRun> LoadOpenRunAsync(AuditableContext db, int runId)
    {
        var run = await db.AssessmentRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId)
                  ?? throw new DataNotFoundException("assessment_runs", runId.ToString());

        if (run.Status == (int)AssessmentStatus.Submitted)
            throw new RuleBrokenException("The run has been submitted; its answers are read-only.",
                RunSubmittedRule);

        return run;
    }

    /// <summary>The question has to be one of the run's own assessment — not merely any question.</summary>
    private static async Task EnsureQuestionOfRunAsync(AuditableContext db, AssessmentRun run, int questionId)
    {
        if (!await db.AssessmentQuestions.AnyAsync(q => q.Id == questionId && q.AssessmentId == run.AssessmentId))
            throw new DataNotFoundException("assessment_questions", questionId.ToString());
    }

    /// <summary>
    /// The answer row for the (run, question) pair, added — unsaved, and unanswered (S44 D3) — when the
    /// assessor comments or attaches before choosing an answer.
    /// </summary>
    private static async Task<AssessmentRunAnswer> GetOrCreateAnswerAsync(AuditableContext db, int runId, int questionId)
    {
        var answer = await db.AssessmentRunAnswers
            .FirstOrDefaultAsync(a => a.AssessmentRunId == runId && a.AssessmentQuestionId == questionId);
        if (answer is not null) return answer;

        answer = new AssessmentRunAnswer
        {
            AssessmentRunId = runId,
            AssessmentQuestionId = questionId,
            AnswerContentJson = null,
            LastUpdatedAt = DateTime.UtcNow
        };
        db.AssessmentRunAnswers.Add(answer);
        return answer;
    }

    private static async Task<Dictionary<string, string>> FileTypeNamesAsync(AuditableContext db)
    {
        var types = await db.FileTypes.AsNoTracking().ToListAsync();
        return types.GroupBy(t => t.Value.ToString()).ToDictionary(g => g.Key, g => g.First().Name);
    }
}
