using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class HostsService
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = null!;

    public string Protocol { get; set; } = null!;

    public int? Port { get; set; }

    public virtual Host Host { get; set; } = null!;

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();
}
