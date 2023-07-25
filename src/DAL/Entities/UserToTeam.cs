using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class UserToTeam
    {
        public int UserId { get; set; }
        public int TeamId { get; set; }
    }
}
