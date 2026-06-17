using System.Collections.Generic;

namespace Model.Assessments;

/// <summary>
/// Result of a dry-run assessment-template import: a validation + summary produced
/// without writing anything to the database. When <see cref="Valid"/> is false the
/// file must not be committed; <see cref="Errors"/> carries the row-level reasons.
/// </summary>
public class AssessmentImportPreview
{
    public bool Valid { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int PageCount { get; set; }

    public int QuestionCount { get; set; }

    /// <summary>Non-blocking observations (e.g. duplicate question text, malformed condition).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Blocking, row-level validation errors. Any entry makes the import invalid.</summary>
    public List<AssessmentImportError> Errors { get; set; } = new();
}

public class AssessmentImportError
{
    /// <summary>1-based source row (Excel) or question index (JSON); 0 for document-level errors.</summary>
    public int Row { get; set; }

    public string Message { get; set; } = string.Empty;
}
