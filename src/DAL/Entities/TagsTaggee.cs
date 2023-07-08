using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class TagsTaggee
    {
        public int TagId { get; set; }
        public int TaggeeId { get; set; }
        public string? Type { get; set; }
    }
}
