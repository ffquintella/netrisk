﻿using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Host
{
    public int Id { get; set; }

    public string? Ip { get; set; }

    public string? HostName { get; set; }

    public short Status { get; set; }

    public string Source { get; set; } = null!;

    public DateTime RegistrationDate { get; set; }

    public DateTime? LastVerificationDate { get; set; }

    public int? TeamId { get; set; }

    public string? Comment { get; set; }

    public virtual Team? Team { get; set; }

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();
}
