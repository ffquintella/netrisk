using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Permission
{
    public int Id { get; set; }

    public string Key { get; set; } = null!;

    public string Name { get; set; } = null!;

    public byte[] Description { get; set; } = null!;

    public int Order { get; set; }

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
