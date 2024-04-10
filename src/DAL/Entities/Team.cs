using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Team
{
    public int Value { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<FixRequest> FixRequests { get; set; } = new List<FixRequest>();

    public virtual ICollection<Host> Hosts { get; set; } = new List<Host>();

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
