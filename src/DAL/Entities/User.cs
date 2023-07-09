using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class User
    {
        public User()
        {
            AddonsNotificationControls = new HashSet<AddonsNotificationControl>();
        }

        public int Value { get; set; }
        public bool? Enabled { get; set; }
        public sbyte Lockout { get; set; }
        public string Type { get; set; } = null!;
        public byte[] Username { get; set; } = null!;
        public string Name { get; set; } = null!;
        public byte[] Email { get; set; } = null!;
        public string? Salt { get; set; }
        public byte[] Password { get; set; } = null!;
        public DateTime? LastLogin { get; set; }
        public DateTime LastPasswordChangeDate { get; set; }
        public int RoleId { get; set; }
        public string? Lang { get; set; }
        public bool Admin { get; set; }
        public int MultiFactor { get; set; }
        public sbyte ChangePassword { get; set; }
        public string CustomDisplaySettings { get; set; } = null!;
        public int? Manager { get; set; }
        public string? CustomPlanMitigationDisplaySettings { get; set; }
        public string? CustomPerformReviewsDisplaySettings { get; set; }
        public string? CustomReviewregularlyDisplaySettings { get; set; }
        public string? CustomRisksAndIssuesSettings { get; set; }

        public virtual ICollection<AddonsNotificationControl> AddonsNotificationControls { get; set; }
    }
}
