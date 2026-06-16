using System;

namespace DAL.Entities;

public partial class IrpTemplateTask
{
    public int Id { get; set; }

    public int IrpTemplateId { get; set; }

    public string Title { get; set; } = null!;

    public string? InstructionsMarkdown { get; set; }

    public string AssigneeRuleJson { get; set; } = null!;

    public int DueOffsetSeconds { get; set; }

    public int? PredecessorTaskId { get; set; }

    public bool RequiresConfirmation { get; set; }

    public virtual IrpTemplate IrpTemplate { get; set; } = null!;

    public virtual IrpTemplateTask? PredecessorTask { get; set; }
}
