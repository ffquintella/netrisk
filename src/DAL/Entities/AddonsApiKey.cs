using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AddonsApiKey
    {
        public uint Id { get; set; }
        public string Name { get; set; } = null!;
        public string Value { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime CreationDate { get; set; }
    }
}
