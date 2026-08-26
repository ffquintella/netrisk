using System;

namespace Model.Configuration;

public class ServerConfiguration
{
    public string Url { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>
    /// Accept a server certificate that does not validate (Track 7 milestone 7.4.1).
    ///
    /// Default false, and it has to stay that way: this used to be unconditional, which removed
    /// transport authentication from every desktop-to-API call. Bound from
    /// <c>Server:AllowInvalidCertificate</c>. The supported route for a private certificate authority
    /// is the operating-system trust store, not this switch — see
    /// <c>Tools.Security.ServerCertificatePolicy</c>.
    /// </summary>
    public bool AllowInvalidCertificate { get; set; }
    //public bool Enabled { get; set; }
    public DateTime Timeout { get; set; }
}