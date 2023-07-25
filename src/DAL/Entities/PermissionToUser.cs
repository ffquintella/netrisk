using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class PermissionToUser
    {
        public int PermissionId { get; set; }
        public int UserId { get; set; }
    }
}
