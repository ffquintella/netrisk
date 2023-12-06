﻿using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class AssessmentRunsAnswer
{
    public int Id { get; set; }

    public int AnswerId { get; set; }

    public string? Value { get; set; }

    public int RunId { get; set; }

    public virtual AssessmentAnswer Answer { get; set; } = null!;

    public virtual AssessmentRun Run { get; set; } = null!;
}
