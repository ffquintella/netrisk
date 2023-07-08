using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AddonsClientRegistration
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string ExternalId { get; set; } = null!;
        public string? Hostname { get; set; }
        public string? LoggedAccount { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTime LastVerificationDate { get; set; }
        public string Status { get; set; } = null!;
    }
}
