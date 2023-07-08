using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Session
    {
        public string Id { get; set; } = null!;
        public uint? Access { get; set; }
        public byte[]? Data { get; set; }
    }
}
