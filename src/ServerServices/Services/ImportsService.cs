using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using Model.Assessments;
using ServerServices.Interfaces;
using ClosedXML.Excel;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

// JSON contract DTOs
public class JsonAssessmentTemplate
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<JsonAssessmentQuestion> Questions { get; set; } = new();
}

public class JsonAssessmentQuestion
{
    public string QuestionText { get; set; } = null!;
    public int Order { get; set; }
    public int PageNumber { get; set; } = 1;
    public string? ConditionJson { get; set; }
    public string? ExplanationMarkdown { get; set; }
}

public class ImportsService(ILogger logger, IDalService dalService, ILocalizationService localization)
    : LocalizableService(logger, dalService, localization), IImportsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    #region PREVIEW (dry-run)

    public Task<AssessmentImportPreview> PreviewAssessmentFromJsonAsync(string jsonContent)
    {
        Logger.Information("Dry-run validating GRC Assessment template from JSON");
        var (_, preview) = ParseJson(jsonContent);
        return Task.FromResult(preview);
    }

    public Task<AssessmentImportPreview> PreviewAssessmentFromExcelAsync(Stream excelStream, string assessmentName)
    {
        Logger.Information("Dry-run validating GRC Assessment template from Excel for '{Name}'", assessmentName);
        var (_, preview) = ParseExcel(excelStream, assessmentName);
        return Task.FromResult(preview);
    }

    #endregion

    #region IMPORT (commit)

    public async Task<Assessment> ImportAssessmentFromJsonAsync(string jsonContent)
    {
        Logger.Information("Starting GRC Assessment Import from JSON");

        var (template, preview) = ParseJson(jsonContent);
        if (!preview.Valid || template == null)
            throw new InvalidOperationException(FormatErrors(preview));

        return await PersistAsync(template);
    }

    public async Task<Assessment> ImportAssessmentFromExcelAsync(Stream excelStream, string assessmentName)
    {
        Logger.Information("Starting GRC Assessment Import from Excel for '{Name}'", assessmentName);

        var (template, preview) = ParseExcel(excelStream, assessmentName);
        if (!preview.Valid || template == null)
            throw new InvalidOperationException(FormatErrors(preview));

        return await PersistAsync(template);
    }

    private async Task<Assessment> PersistAsync(JsonAssessmentTemplate template)
    {
        try
        {
            using var dbContext = DalService.GetContext();

            var assessment = new Assessment
            {
                Name = template.Name,
                Created = DateTime.UtcNow
            };

            dbContext.Assessments.Add(assessment);
            await dbContext.SaveChangesAsync(); // Generates assessment ID

            foreach (var q in template.Questions)
            {
                dbContext.AssessmentQuestions.Add(new AssessmentQuestion
                {
                    AssessmentId = assessment.Id,
                    Question = q.QuestionText,
                    Order = q.Order,
                    PageNumber = q.PageNumber,
                    ConditionJson = q.ConditionJson,
                    ExplanationMarkdown = q.ExplanationMarkdown
                });
            }

            await dbContext.SaveChangesAsync();
            Logger.Information("Successfully imported assessment '{Name}' with {Count} questions", template.Name, template.Questions.Count);

            return assessment;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error persisting imported GRC assessment");
            throw;
        }
    }

    #endregion

    #region PARSE + VALIDATE

    private (JsonAssessmentTemplate? template, AssessmentImportPreview preview) ParseJson(string jsonContent)
    {
        var preview = new AssessmentImportPreview();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "JSON content is empty." });
            return (null, Finalize(preview));
        }

        JsonAssessmentTemplate? template;
        try
        {
            template = JsonSerializer.Deserialize<JsonAssessmentTemplate>(jsonContent, JsonOptions);
        }
        catch (JsonException ex)
        {
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = $"Malformed JSON: {ex.Message}" });
            return (null, Finalize(preview));
        }

        if (template == null)
        {
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "JSON did not deserialize to a template." });
            return (null, Finalize(preview));
        }

        preview.Name = template.Name;
        preview.Description = template.Description;

        if (string.IsNullOrWhiteSpace(template.Name))
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "Assessment name is required." });

        // Normalise + validate each question (1-based index in the JSON array).
        var index = 0;
        foreach (var q in template.Questions)
        {
            index++;
            if (string.IsNullOrWhiteSpace(q.QuestionText))
            {
                preview.Errors.Add(new AssessmentImportError { Row = index, Message = "Question text is required." });
                continue;
            }

            if (q.PageNumber < 1)
            {
                preview.Warnings.Add($"Question {index}: page number {q.PageNumber} is invalid; defaulting to 1.");
                q.PageNumber = 1;
            }

            ValidateCondition(q.ConditionJson, index, preview);
        }

        ValidateQuestionSet(template.Questions.Select(x => x.QuestionText), preview);

        var validJson = template.Questions.Where(q => !string.IsNullOrWhiteSpace(q.QuestionText)).ToList();
        preview.QuestionCount = validJson.Count;
        preview.PageCount = validJson.Select(q => q.PageNumber).Distinct().Count();

        return (template, Finalize(preview));
    }

    private (JsonAssessmentTemplate? template, AssessmentImportPreview preview) ParseExcel(Stream excelStream, string assessmentName)
    {
        var preview = new AssessmentImportPreview { Name = assessmentName };

        if (excelStream == null)
        {
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "Excel stream is null." });
            return (null, Finalize(preview));
        }

        if (string.IsNullOrWhiteSpace(assessmentName))
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "Assessment name is required." });

        var template = new JsonAssessmentTemplate { Name = assessmentName };

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
            {
                preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "The workbook does not contain any worksheets." });
                return (null, Finalize(preview));
            }

            // Row 1 is the header: Page, Question, Order, Condition, Explanation
            foreach (var row in worksheet.RowsUsed().Skip(1))
            {
                var rowNumber = row.RowNumber();
                var questionText = row.Cell(2).Value.ToString();

                // A fully empty trailing row is skipped silently; a row with data but no
                // question text is a row-level error.
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    var hasOtherData = !string.IsNullOrWhiteSpace(row.Cell(1).Value.ToString())
                                       || !string.IsNullOrWhiteSpace(row.Cell(3).Value.ToString())
                                       || !string.IsNullOrWhiteSpace(row.Cell(4).Value.ToString())
                                       || !string.IsNullOrWhiteSpace(row.Cell(5).Value.ToString());
                    if (hasOtherData)
                        preview.Errors.Add(new AssessmentImportError { Row = rowNumber, Message = "Question text is required." });
                    continue;
                }

                var pageNumber = 1;
                var pageStr = row.Cell(1).Value.ToString();
                if (!string.IsNullOrWhiteSpace(pageStr) && !int.TryParse(pageStr, out pageNumber))
                {
                    preview.Warnings.Add($"Row {rowNumber}: page '{pageStr}' is not a number; defaulting to 1.");
                    pageNumber = 1;
                }
                if (pageNumber < 1) pageNumber = 1;

                var order = 999999;
                var orderStr = row.Cell(3).Value.ToString();
                if (!string.IsNullOrWhiteSpace(orderStr) && !int.TryParse(orderStr, out order))
                {
                    preview.Warnings.Add($"Row {rowNumber}: order '{orderStr}' is not a number; defaulting to last.");
                    order = 999999;
                }

                var conditionText = row.Cell(4).Value.ToString();
                var explanationText = row.Cell(5).Value.ToString();

                ValidateCondition(conditionText, rowNumber, preview);

                template.Questions.Add(new JsonAssessmentQuestion
                {
                    QuestionText = questionText,
                    Order = order,
                    PageNumber = pageNumber,
                    ConditionJson = string.IsNullOrWhiteSpace(conditionText) ? null : conditionText,
                    ExplanationMarkdown = string.IsNullOrWhiteSpace(explanationText) ? null : explanationText
                });
            }
        }
        catch (Exception ex)
        {
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = $"Could not read the Excel file: {ex.Message}" });
            return (null, Finalize(preview));
        }

        ValidateQuestionSet(template.Questions.Select(x => x.QuestionText), preview);

        preview.QuestionCount = template.Questions.Count;
        preview.PageCount = template.Questions.Select(q => q.PageNumber).Distinct().Count();

        return (template, Finalize(preview));
    }

    private static void ValidateCondition(string? conditionJson, int row, AssessmentImportPreview preview)
    {
        if (string.IsNullOrWhiteSpace(conditionJson)) return;
        try
        {
            var condition = JsonSerializer.Deserialize<QuestionCondition>(conditionJson, JsonOptions);
            if (condition == null || condition.QuestionId <= 0)
                preview.Warnings.Add($"Row {row}: condition does not reference a valid question id and will be ignored.");
        }
        catch (JsonException)
        {
            preview.Warnings.Add($"Row {row}: condition is not valid JSON and will be ignored.");
        }
    }

    private static void ValidateQuestionSet(IEnumerable<string> questionTexts, AssessmentImportPreview preview)
    {
        var texts = questionTexts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        if (texts.Count == 0 && preview.Errors.All(e => e.Row != 0))
            preview.Errors.Add(new AssessmentImportError { Row = 0, Message = "The template contains no questions." });

        var duplicates = texts
            .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var dup in duplicates)
            preview.Warnings.Add($"Duplicate question text: '{dup}'.");
    }

    private static AssessmentImportPreview Finalize(AssessmentImportPreview preview)
    {
        // An invalid file imports nothing, so it must not advertise a question/page count.
        preview.Valid = preview.Errors.Count == 0;
        if (!preview.Valid)
        {
            preview.QuestionCount = 0;
            preview.PageCount = 0;
        }
        return preview;
    }

    private static string FormatErrors(AssessmentImportPreview preview)
    {
        var lines = preview.Errors.Select(e => e.Row > 0 ? $"Row {e.Row}: {e.Message}" : e.Message);
        return "Import validation failed. " + string.Join(" ", lines);
    }

    #endregion
}
