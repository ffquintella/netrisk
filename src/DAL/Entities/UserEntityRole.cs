using System;

namespace DAL.Entities;

public partial class UserEntityRole
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int EntityId { get; set; }

    public int RoleId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Entity Entity { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;
}
