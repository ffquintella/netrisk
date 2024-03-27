using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Message
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public string? Message1 { get; set; }

    public int? Status { get; set; }

    public int? ChatId { get; set; }

    public virtual User User { get; set; } = null!;
}
