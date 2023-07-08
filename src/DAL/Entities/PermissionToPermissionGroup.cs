using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class PermissionToPermissionGroup
    {
        public int PermissionId { get; set; }
        public int PermissionGroupId { get; set; }
    }
}
