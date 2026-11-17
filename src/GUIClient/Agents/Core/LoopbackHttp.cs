using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GUIClient.Agents;

/// <summary>A parsed request: method, target, headers (case-insensitive, each at most once) and the body.</summary>
public sealed class LoopbackHttpRequest
{
    internal LoopbackHttpRequest(string method, string target, Dictionary<string, string> headers, byte[] body)
    {
        Method = method;
        Target = target;
        Headers = headers;
        Body = body;
    }

    public string Method { get; }
    public string Target { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public byte[] Body { get; }

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

public enum HttpParseError
{
    None,
    ConnectionClosed,
    Malformed,
    HeaderTooLarge,
    UnsupportedVersion,
    DuplicateHeader,
    TransferEncodingNotSupported,
    LengthRequired,
    BodyTooLarge
}

/// <summary>
/// The HTTP/1.1 subset the loopback MCP endpoint speaks: one request per connection, bodies by Content-Length only,
/// every size bounded. Hand-rolled rather than hosting Kestrel in the desktop process (see the S55 implementation
/// notes); deliberately strict, because anything it accepts reaches the agent layer.
/// </summary>
public static class LoopbackHttp
{
    public const int MaxHeaderBytes = 16 * 1024;
    public const int MaxHeaderCount = 64;
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>Headers that mean one thing only if they appear once. A repeat is refused, not merged.</summary>
    private static readonly HashSet<string> SingleValued = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "content-length", "content-type", "origin", "authorization", "mcp-session-id", "mcp-protocol-version",
        LoopbackRequestGuard.LaunchTokenHeader
    };

    public static async Task<(LoopbackHttpRequest? Request, HttpParseError Error)> ReadRequestAsync(Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeaderBytes];
        var filled = 0;
        var headEnd = -1;
        while (headEnd < 0)
        {
            if (filled == buffer.Length) return (null, HttpParseError.HeaderTooLarge);
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0) return (null, filled == 0 ? HttpParseError.ConnectionClosed : HttpParseError.Malformed);
            var searchFrom = Math.Max(0, filled - 3);
            filled += read;
            headEnd = IndexOfHeadEnd(buffer, searchFrom, filled);
        }

        var head = Encoding.Latin1.GetString(buffer, 0, headEnd);
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || requestLine[0].Length == 0 || !IsToken(requestLine[0]))
            return (null, HttpParseError.Malformed);
        if (requestLine[2] != "HTTP/1.1") return (null, HttpParseError.UnsupportedVersion);
        // Origin-form only: an absolute target is a proxy request, and this is not a proxy.
        if (!requestLine[1].StartsWith('/')) return (null, HttpParseError.Malformed);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lines.Length - 1 > MaxHeaderCount) return (null, HttpParseError.HeaderTooLarge);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            // No obsolete line folding, no whitespace before the colon, no control characters.
            if (colon <= 0 || line[0] is ' ' or '\t' || !IsToken(line[..colon])) return (null, HttpParseError.Malformed);
            var value = line[(colon + 1)..].Trim(' ', '\t');
            if (HasControl(value)) return (null, HttpParseError.Malformed);

            var name = line[..colon];
            if (headers.ContainsKey(name))
            {
                if (SingleValued.Contains(name)) return (null, HttpParseError.DuplicateHeader);
                headers[name] += ", " + value;
            }
            else
            {
                headers[name] = value;
            }
        }

        if (headers.ContainsKey("transfer-encoding")) return (null, HttpParseError.TransferEncodingNotSupported);

        var bodyLength = 0;
        if (headers.TryGetValue("content-length", out var lengthText))
        {
            if (!int.TryParse(lengthText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out bodyLength))
                return (null, HttpParseError.Malformed);
            if (bodyLength > MaxBodyBytes) return (null, HttpParseError.BodyTooLarge);
        }
        else if (requestLine[0] == "POST")
        {
            return (null, HttpParseError.LengthRequired);
        }

        var body = new byte[bodyLength];
        var already = Math.Min(filled - (headEnd + 4), bodyLength);
        Array.Copy(buffer, headEnd + 4, body, 0, already);
        var position = already;
        while (position < bodyLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(position), cancellationToken).ConfigureAwait(false);
            if (read == 0) return (null, HttpParseError.Malformed);
            position += read;
        }

        return (new LoopbackHttpRequest(requestLine[0], requestLine[1], headers, body), HttpParseError.None);
    }

    /// <summary>Writes a complete response and asks the client to close. Never adds CORS headers.</summary>
    public static async Task WriteResponseAsync(Stream stream, int status, string? body,
        IReadOnlyDictionary<string, string>? extraHeaders, CancellationToken cancellationToken)
    {
        var payload = body is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n");
        if (body is not null) head.Append("Content-Type: application/json; charset=utf-8\r\n");
        head.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
        head.Append("Cache-Control: no-store\r\n");
        head.Append("X-Content-Type-Options: nosniff\r\n");
        head.Append("Connection: close\r\n");
        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                if (HasControl(name) || HasControl(value)) continue;
                head.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0) await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static int StatusFor(HttpParseError error) => error switch
    {
        HttpParseError.HeaderTooLarge => 431,
        HttpParseError.UnsupportedVersion => 505,
        HttpParseError.TransferEncodingNotSupported => 501,
        HttpParseError.LengthRequired => 411,
        HttpParseError.BodyTooLarge => 413,
        _ => 400
    };

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        411 => "Length Required",
        413 => "Content Too Large",
        415 => "Unsupported Media Type",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        505 => "HTTP Version Not Supported",
        _ => "Status"
    };

    private static int IndexOfHeadEnd(byte[] buffer, int from, int to)
    {
        for (var i = from; i + 3 < to; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n') return i;
        }
        return -1;
    }

    private static bool IsToken(string value)
    {
        foreach (var ch in value)
        {
            if (ch <= 32 || ch >= 127 || "()<>@,;:\\\"/[]?={}".IndexOf(ch) >= 0) return false;
        }
        return value.Length > 0;
    }

    private static bool HasControl(string value)
    {
        foreach (var ch in value)
        {
            if ((ch < 32 && ch != '\t') || ch == 127) return true;
        }
        return false;
    }
}
