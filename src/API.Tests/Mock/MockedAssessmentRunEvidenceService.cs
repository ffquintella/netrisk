using System;
using System.Collections.Generic;
using DAL.Entities;
using Model.Assessments;
using Model.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IAssessmentRunEvidenceService"/> for <c>AssessmentRunEvidenceControllerTest</c>
/// (GitHub #80, S44). The run id drives every branch of the controller's error mapping: <see cref="Invalid"/>
/// is a refused parameter, <see cref="NotUploader"/> a delete by somebody else, <see cref="Missing"/> a run
/// that does not exist, <see cref="Submitted"/> a submitted run and <see cref="Broken"/> an unexpected
/// failure; any other run succeeds.
/// </summary>
public static class MockedAssessmentRunEvidenceService
{
    public const int Open = 10;
    public const int Invalid = 400;
    public const int NotUploader = 403;
    public const int Missing = 404;
    public const int Submitted = 409;
    public const int Broken = 500;

    private static Exception FailureFor(int runId) => runId switch
    {
        Invalid => new InvalidParameterException("comment", "The comment may be at most 4000 characters."),
        NotUploader => new PermissionInvalidException("evidence_owner", 1, "delete assessment evidence"),
        Missing => new DataNotFoundException("assessment_runs", runId.ToString()),
        Submitted => new RuleBrokenException("The run has been submitted; its answers are read-only.", "run_submitted"),
        _ => new InvalidOperationException("boom")
    };

    private static readonly int[] Failing = [Invalid, NotUploader, Missing, Submitted, Broken];

    public static AssessmentAnswerEvidence EvidenceFor(int questionId) => new()
    {
        Name = "badge.png", UniqueName = "u-1", Type = "image/png", OwnerId = 1, Size = 3,
        QuestionId = questionId, AssessmentRunAnswerId = 50, Timestamp = new DateTime(2026, 10, 7)
    };

    public static IAssessmentRunEvidenceService Create()
    {
        var service = Substitute.For<IAssessmentRunEvidenceService>();

        service.SaveCommentAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(call => new AssessmentRunAnswer
            {
                Id = 50, AssessmentRunId = call.ArgAt<int>(0), AssessmentQuestionId = call.ArgAt<int>(1),
                Comment = call.ArgAt<string?>(2), LastUpdatedAt = new DateTime(2026, 10, 7)
            });

        service.GetRunEvidenceAsync(Arg.Any<int>())
            .Returns(new List<AssessmentAnswerEvidence> { EvidenceFor(100), EvidenceFor(101) });

        service.AttachEvidenceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AssessmentEvidenceUploadRequest>(),
                Arg.Any<User>())
            .Returns(call => EvidenceFor(call.ArgAt<int>(1)));

        foreach (var run in Failing)
        {
            var failure = FailureFor(run);
            service.SaveCommentAsync(run, Arg.Any<int>(), Arg.Any<string?>()).ThrowsAsync(failure);
            service.GetRunEvidenceAsync(run).ThrowsAsync(failure);
            service.AttachEvidenceAsync(run, Arg.Any<int>(), Arg.Any<AssessmentEvidenceUploadRequest>(),
                Arg.Any<User>()).ThrowsAsync(failure);
            service.DeleteEvidenceAsync(run, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<User>()).ThrowsAsync(failure);
        }

        return service;
    }
}
