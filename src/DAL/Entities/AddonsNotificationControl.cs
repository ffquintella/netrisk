using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AddonsNotificationControl
    {
        public uint Id { get; set; }
        public string NotificationHash { get; set; } = null!;
        public int NotifiedId { get; set; }
        public DateTime? SentDate { get; set; }

        public virtual User Notified { get; set; } = null!;
    }
}
