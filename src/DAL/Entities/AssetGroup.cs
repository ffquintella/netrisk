using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssetGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
