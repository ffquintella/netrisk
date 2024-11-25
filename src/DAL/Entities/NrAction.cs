using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class NrAction
{
    public int Id { get; set; }

    public string ObjectType { get; set; } = null!;

    public DateTime DateTime { get; set; }

    public int? UserId { get; set; }

    public string? Message { get; set; }

    public virtual User? User { get; set; }

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();
    
    public Incident? Incident { get; set; }
    
    public int? IncidentId { get; set; }
}
