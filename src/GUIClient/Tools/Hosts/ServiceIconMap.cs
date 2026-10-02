using System;
using System.Collections.Generic;
using Material.Icons;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// Which icon a host service gets, by its well-known name and then its port (S38 §3.4).
///
/// Names win over ports: a scanner that says <c>http</c> on port 8000 knows more than the port
/// does. A name the table does not know — including Nessus's catch-all <c>general</c> — falls back
/// to the port, and anything still unmatched is the generic network glyph.
///
/// <see cref="MaterialIconKind"/> is a plain enum in the Avalonia-free <c>Material.Icons</c>
/// package, so this compiles in <c>GUIClient.Tests</c> and the test can prove every kind it returns
/// exists — a misspelt kind would otherwise fail only when the chip renders.
/// </summary>
public static class ServiceIconMap
{
    /// <summary>The icon for a service nothing in the table recognises.</summary>
    public const MaterialIconKind Fallback = MaterialIconKind.Lan;

    private static readonly Dictionary<string, MaterialIconKind> ByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ssh"] = MaterialIconKind.Console,
            ["telnet"] = MaterialIconKind.Console,
            ["shell"] = MaterialIconKind.Console,

            ["http"] = MaterialIconKind.Web,
            ["https"] = MaterialIconKind.Web,
            ["www"] = MaterialIconKind.Web,
            ["http-alt"] = MaterialIconKind.Web,
            ["http-proxy"] = MaterialIconKind.Web,
            ["https-alt"] = MaterialIconKind.Web,

            ["smtp"] = MaterialIconKind.Email,
            ["smtps"] = MaterialIconKind.Email,
            ["submission"] = MaterialIconKind.Email,
            ["imap"] = MaterialIconKind.Email,
            ["imaps"] = MaterialIconKind.Email,
            ["pop3"] = MaterialIconKind.Email,
            ["pop3s"] = MaterialIconKind.Email,

            ["dns"] = MaterialIconKind.Dns,
            ["domain"] = MaterialIconKind.Dns,

            ["mysql"] = MaterialIconKind.Database,
            ["mariadb"] = MaterialIconKind.Database,
            ["mssql"] = MaterialIconKind.Database,
            ["ms-sql-s"] = MaterialIconKind.Database,
            ["postgres"] = MaterialIconKind.Database,
            ["postgresql"] = MaterialIconKind.Database,
            ["mongodb"] = MaterialIconKind.Database,
            ["oracle"] = MaterialIconKind.Database,
            ["oracle-tns"] = MaterialIconKind.Database,
            ["redis"] = MaterialIconKind.Database,

            ["rdp"] = MaterialIconKind.RemoteDesktop,
            ["vnc"] = MaterialIconKind.RemoteDesktop,
            ["ms-wbt-server"] = MaterialIconKind.RemoteDesktop,

            ["ldap"] = MaterialIconKind.AccountKey,
            ["ldaps"] = MaterialIconKind.AccountKey,
            ["kerberos"] = MaterialIconKind.AccountKey,
            ["kerberos-sec"] = MaterialIconKind.AccountKey,

            ["smb"] = MaterialIconKind.FolderNetwork,
            ["cifs"] = MaterialIconKind.FolderNetwork,
            ["netbios"] = MaterialIconKind.FolderNetwork,
            ["netbios-ssn"] = MaterialIconKind.FolderNetwork,
            ["netbios-ns"] = MaterialIconKind.FolderNetwork,
            ["microsoft-ds"] = MaterialIconKind.FolderNetwork,

            ["snmp"] = MaterialIconKind.Lan,
            ["ntp"] = MaterialIconKind.Lan,
        };

    private static readonly Dictionary<int, MaterialIconKind> ByPort = new()
    {
        [22] = MaterialIconKind.Console, [23] = MaterialIconKind.Console, [514] = MaterialIconKind.Console,

        [80] = MaterialIconKind.Web, [443] = MaterialIconKind.Web,
        [8080] = MaterialIconKind.Web, [8443] = MaterialIconKind.Web,

        [25] = MaterialIconKind.Email, [143] = MaterialIconKind.Email, [110] = MaterialIconKind.Email,
        [465] = MaterialIconKind.Email, [587] = MaterialIconKind.Email, [993] = MaterialIconKind.Email,
        [995] = MaterialIconKind.Email,

        [53] = MaterialIconKind.Dns,

        [3306] = MaterialIconKind.Database, [1433] = MaterialIconKind.Database,
        [5432] = MaterialIconKind.Database, [27017] = MaterialIconKind.Database,
        [1521] = MaterialIconKind.Database, [6379] = MaterialIconKind.Database,

        [3389] = MaterialIconKind.RemoteDesktop, [5900] = MaterialIconKind.RemoteDesktop,

        [389] = MaterialIconKind.AccountKey, [636] = MaterialIconKind.AccountKey,
        [88] = MaterialIconKind.AccountKey,

        [139] = MaterialIconKind.FolderNetwork, [445] = MaterialIconKind.FolderNetwork,

        [161] = MaterialIconKind.Lan, [123] = MaterialIconKind.Lan,
    };

    /// <summary>Every kind the map can return, for the test that proves each one exists.</summary>
    public static IEnumerable<MaterialIconKind> AllKinds()
    {
        foreach (var kind in ByName.Values) yield return kind;
        foreach (var kind in ByPort.Values) yield return kind;
        yield return Fallback;
    }

    /// <summary>The icon for one service.</summary>
    /// <param name="name">The scanner's service name (<c>www</c>, <c>ssl/http</c>, <c>general</c>).</param>
    /// <param name="port">The port, or null/0 when the service has none.</param>
    public static MaterialIconKind KindFor(string? name, int? port)
    {
        var normalized = Normalize(name);

        if (normalized.Length > 0 && ByName.TryGetValue(normalized, out var byName))
            return byName;

        if (port is > 0 && ByPort.TryGetValue(port.Value, out var byPort))
            return byPort;

        return Fallback;
    }

    /// <summary>
    /// Lower-case, trimmed, with a TLS wrapper prefix (<c>ssl/http</c>, <c>tls/imap</c>) and a
    /// trailing <c>?</c> (nmap's "probably") removed — the wrapper says how, the rest says what.
    /// </summary>
    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var value = name.Trim().TrimEnd('?').ToLowerInvariant();

        foreach (var prefix in new[] { "ssl/", "tls/" })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                value = value[prefix.Length..];
        }

        return value;
    }
}
