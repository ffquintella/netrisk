using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class PermissionGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public byte[] Description { get; set; } = null!;
        public int Order { get; set; }
    }
}
