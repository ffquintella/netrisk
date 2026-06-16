using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.Extensions.Localization;
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

    public async Task<Assessment> ImportAssessmentFromJsonAsync(string jsonContent)
    {
        Logger.Information("Starting GRC Assessment Import from JSON");

        if (string.IsNullOrEmpty(jsonContent))
            throw new ArgumentException("JSON content cannot be empty", nameof(jsonContent));

        try
        {
            var template = JsonSerializer.Deserialize<JsonAssessmentTemplate>(jsonContent, JsonOptions);
            if (template == null || string.IsNullOrEmpty(template.Name))
                throw new InvalidOperationException("Invalid template JSON. Assessment name is required.");

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
                var question = new AssessmentQuestion
                {
                    AssessmentId = assessment.Id,
                    Question = q.QuestionText,
                    Order = q.Order,
                    PageNumber = q.PageNumber,
                    ConditionJson = q.ConditionJson,
                    ExplanationMarkdown = q.ExplanationMarkdown
                };
                dbContext.AssessmentQuestions.Add(question);
            }

            await dbContext.SaveChangesAsync();
            Logger.Information("Successfully imported JSON assessment '{Name}' with {Count} questions", template.Name, template.Questions.Count);

            return assessment;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error importing GRC assessment from JSON");
            throw;
        }
    }

    public async Task<Assessment> ImportAssessmentFromExcelAsync(Stream excelStream, string assessmentName)
    {
        Logger.Information("Starting GRC Assessment Import from Excel for '{Name}'", assessmentName);

        if (excelStream == null)
            throw new ArgumentNullException(nameof(excelStream));
        if (string.IsNullOrEmpty(assessmentName))
            throw new ArgumentException("Assessment name cannot be empty", nameof(assessmentName));

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
                throw new InvalidOperationException("The Excel workbook does not contain any worksheets");

            using var dbContext = DalService.GetContext();

            var assessment = new Assessment
            {
                Name = assessmentName,
                Created = DateTime.UtcNow
            };

            dbContext.Assessments.Add(assessment);
            await dbContext.SaveChangesAsync(); // Generates assessment ID

            // Assume Row 1 is Headers: Page, Question, Order, Condition, Explanation
            var rows = worksheet.RowsUsed().Skip(1);
            int count = 0;

            foreach (var row in rows)
            {
                var pageCell = row.Cell(1).Value;
                var questionCell = row.Cell(2).Value;
                var orderCell = row.Cell(3).Value;
                var conditionCell = row.Cell(4).Value;
                var explanationCell = row.Cell(5).Value;

                var questionText = questionCell.ToString();
                if (string.IsNullOrEmpty(questionText))
                    continue; // Skip empty question rows gracefully

                int pageNumber = 1;
                var pageStr = pageCell.ToString();
                if (!string.IsNullOrEmpty(pageStr) && int.TryParse(pageStr, out var pNum))
                {
                    pageNumber = pNum;
                }

                int order = 999999;
                var orderStr = orderCell.ToString();
                if (!string.IsNullOrEmpty(orderStr) && int.TryParse(orderStr, out var ord))
                {
                    order = ord;
                }

                var conditionText = conditionCell.ToString();
                var explanationText = explanationCell.ToString();

                var question = new AssessmentQuestion
                {
                    AssessmentId = assessment.Id,
                    Question = questionText,
                    Order = order,
                    PageNumber = pageNumber,
                    ConditionJson = string.IsNullOrEmpty(conditionText) ? null : conditionText,
                    ExplanationMarkdown = string.IsNullOrEmpty(explanationText) ? null : explanationText
                };

                dbContext.AssessmentQuestions.Add(question);
                count++;
            }

            await dbContext.SaveChangesAsync();
            Logger.Information("Successfully imported Excel assessment '{Name}' with {Count} questions", assessmentName, count);

            return assessment;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error importing GRC assessment from Excel");
            throw;
        }
    }
}
