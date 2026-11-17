using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GUIClient.Agents;

/// <summary>Why a request was refused before anything read its body.</summary>
public enum LoopbackRefusal
{
    None,
    NotLoopbackPeer,
    MissingHost,
    ForeignHost,
    ForeignOrigin,
    MissingLaunchToken,
    BadLaunchToken
}

/// <summary>
/// The checks every request passes before the MCP layer sees it (T344, S55 "Transport and local security"): the peer
/// is loopback, <c>Host</c> is the loopback literal and port, <c>Origin</c> is absent or our own (DNS-rebinding
/// defence; no CORS), and the per-launch token from the user-only discovery file is presented.
/// <c>AgentLoopbackGuardTest</c> is the evidence.
/// </summary>
public sealed class LoopbackRequestGuard
{
    public const string LaunchTokenHeader = "X-NetRisk-Launch-Token";

    private readonly byte[] _launchToken;

    public LoopbackRequestGuard(int port, string launchToken)
    {
        if (port is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        ArgumentException.ThrowIfNullOrEmpty(launchToken);
        Port = port;
        _launchToken = Encoding.UTF8.GetBytes(launchToken);
    }

    public int Port { get; }

    /// <summary>The only <c>Host</c> accepted: <c>127.0.0.1:port</c>. Not <c>localhost</c>, not a name of any kind.</summary>
    public string Authority => "127.0.0.1:" + Port;

    /// <summary>The only <c>Origin</c> accepted when one is sent: our own.</summary>
    public string Origin => "http://127.0.0.1:" + Port;

    public LoopbackRefusal Check(IPAddress? peer, string? host, string? origin, string? launchToken)
    {
        if (peer is null || !IPAddress.IsLoopback(peer)) return LoopbackRefusal.NotLoopbackPeer;
        if (string.IsNullOrEmpty(host)) return LoopbackRefusal.MissingHost;
        if (!string.Equals(host.Trim(), Authority, StringComparison.OrdinalIgnoreCase)) return LoopbackRefusal.ForeignHost;
        if (origin is not null && !string.Equals(origin.Trim(), Origin, StringComparison.OrdinalIgnoreCase))
            return LoopbackRefusal.ForeignOrigin;
        if (string.IsNullOrEmpty(launchToken)) return LoopbackRefusal.MissingLaunchToken;

        var presented = Encoding.UTF8.GetBytes(launchToken.Trim());
        return CryptographicOperations.FixedTimeEquals(presented, _launchToken)
            ? LoopbackRefusal.None
            : LoopbackRefusal.BadLaunchToken;
    }
}
