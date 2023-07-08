using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class UserMfa
    {
        public int Uid { get; set; }
        public int? Verified { get; set; }
        public string? Secret { get; set; }
    }
}
