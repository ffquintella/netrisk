using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Serilog;
using ServerServices.Http;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track7;

/// <summary>
/// <see cref="OutboundHttpRequest.MaxResponseBytes"/>, observed against a real socket that answers
/// with more than the caller will take.
///
/// Observed rather than unit-tested on a flag, for the same reason the TLS behaviour is: the cap is
/// not a property, it is the claim that the body never fully lands in memory. A test that asserted
/// the field was copied onto the request would pass against the pre-fix client, which buffered the
/// whole body inside <c>HttpClient</c> before any code here saw it — precisely the "documented
/// control that does nothing" this repository has shipped before. So the server here counts the
/// bytes it actually managed to write, and the test asserts the client walked away long before the
/// end of a body it declared no length for.
///
/// Plain HTTP on 127.0.0.1, which <see cref="OutboundUrlPolicy"/> allows with private networks
/// unblocked, so the destination policy is not in the way.
/// </summary>
[TestSubject(typeof(OutboundHttpClient))]
public class OutboundHttpResponseSizeTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static OutboundHttpClient Client() =>
        new(Log, new OutboundUrlPolicy(Log, blockPrivateNetworks: false));

    /// <summary>
    /// A body under the cap is untouched — the fix must not truncate or re-encode a normal response.
    /// </summary>
    [Fact]
    public async Task ReadsAResponseUnderTheCapUnchanged()
    {
        var body = new string('a', 4096);

        using var server = new ChunkedHttpServer(body, declareContentLength: true);
        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseBytes = 8192
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(body, response.Body);
    }

    /// <summary>
    /// A body exactly at the cap still succeeds: the limit is a maximum, not a strict inequality one
    /// byte early.
    /// </summary>
    [Fact]
    public async Task AcceptsABodyExactlyAtTheCap()
    {
        var body = new string('b', 1024);

        using var server = new ChunkedHttpServer(body, declareContentLength: true);
        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseBytes = 1024
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(body, response.Body);
    }

    /// <summary>
    /// The one that fails on the pre-fix code.
    ///
    /// The server declares no <c>Content-Length</c> and streams 64 MiB, so nothing can be refused up
    /// front — the only way to survive it is to stop reading. Pre-fix, <c>ReadAsStringAsync</c>
    /// would have returned the whole 64 MiB body and the status would have been 200.
    /// </summary>
    [Fact]
    public async Task RejectsAnUndeclaredBodyOverTheCapWithoutBufferingIt()
    {
        const int cap = 64 * 1024;
        const int offered = 64 * 1024 * 1024;

        using var server = new ChunkedHttpServer(offered, declareContentLength: false);
        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseBytes = cap
        });

        Assert.Equal(0, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.Null(response.Body);
        Assert.Contains(cap.ToString(CultureInfo.InvariantCulture), response.TransportError!);

        // The client gave up while reading rather than after: the server never got to write anything
        // close to the body it was offering. A generous multiple of the cap, because the socket and
        // the handler both hold buffers the client cannot refuse — the assertion that matters is
        // that it is nowhere near 64 MiB.
        await server.Finished;
        Assert.True(server.BytesWritten < offered / 4,
            $"the server wrote {server.BytesWritten} of {offered} bytes, so the body was not abandoned early");
    }

    /// <summary>
    /// A declared <c>Content-Length</c> over the cap is refused before the body is read at all — the
    /// cheap rejection, and the one an honest remote with too much data will hit.
    /// </summary>
    [Fact]
    public async Task RejectsADeclaredContentLengthOverTheCapBeforeReadingTheBody()
    {
        const int cap = 1024;

        using var server = new ChunkedHttpServer(16 * 1024, declareContentLength: true);
        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseBytes = cap
        });

        Assert.Equal(0, response.StatusCode);
        Assert.Null(response.Body);
        Assert.Contains("16384", response.TransportError!);
        Assert.Contains(cap.ToString(CultureInfo.InvariantCulture), response.TransportError!);
    }

    /// <summary>
    /// The default is the one nearly every call site uses, so it has to be a real number rather than
    /// "unbounded by omission" — the pre-fix behaviour a caller that sets nothing would inherit.
    /// </summary>
    [Fact]
    public void DefaultsToASixteenMebibyteCap()
    {
        var request = new OutboundHttpRequest { Method = "GET", Url = "https://example.test/" };

        Assert.Equal(16L * 1024 * 1024, request.MaxResponseBytes);
        Assert.Equal(OutboundHttpRequest.DefaultMaxResponseBytes, request.MaxResponseBytes);
    }

    /// <summary>A cap of zero or less is a caller mistake, not a request for an empty body.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void RefusesANonPositiveCap(long cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutboundHttpRequest { Method = "GET", Url = "https://example.test/", MaxResponseBytes = cap });
    }

    /// <summary>
    /// A loopback HTTP listener that writes a body of a chosen size, with or without declaring its
    /// length, and records how much of it it actually got out before the client hung up.
    /// </summary>
    private sealed class ChunkedHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly byte[]? _exactBody;
        private readonly int _size;
        private readonly bool _declareContentLength;

        private long _bytesWritten;

        public ChunkedHttpServer(string body, bool declareContentLength)
            : this(Encoding.UTF8.GetByteCount(body), declareContentLength)
        {
            _exactBody = Encoding.UTF8.GetBytes(body);
        }

        public ChunkedHttpServer(int size, bool declareContentLength)
        {
            _size = size;
            _declareContentLength = declareContentLength;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

            _ = Task.Run(ServeAsync);
        }

        public string Url { get; }

        /// <summary>Bytes of the body the server managed to write before the client stopped reading.</summary>
        public long BytesWritten => Interlocked.Read(ref _bytesWritten);

        /// <summary>Completes once the server has stopped writing, successfully or not.</summary>
        public Task Finished => _finished.Task;

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();

                var request = new byte[4096];
                await stream.ReadAsync(request, _stop.Token);

                var head = new StringBuilder("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\n");

                if (_declareContentLength) head.Append(CultureInfo.InvariantCulture, $"Content-Length: {_size}\r\n");
                else head.Append("Transfer-Encoding: chunked\r\n");

                head.Append("Connection: close\r\n\r\n");

                await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token);

                if (_exactBody is not null)
                {
                    await WriteBodyAsync(stream, _exactBody);
                }
                else
                {
                    var chunk = new byte[32 * 1024];
                    Array.Fill(chunk, (byte)'x');

                    for (var written = 0; written < _size; written += chunk.Length)
                        await WriteBodyAsync(stream, chunk);
                }

                if (!_declareContentLength)
                    await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), _stop.Token);

                await stream.FlushAsync(_stop.Token);
            }
            catch (Exception)
            {
                // A client that hung up mid-body is the point of one of these tests, not a failure.
            }
            finally
            {
                _finished.TrySetResult();
            }
        }

        private async Task WriteBodyAsync(NetworkStream stream, byte[] payload)
        {
            if (_declareContentLength)
            {
                await stream.WriteAsync(payload, _stop.Token);
            }
            else
            {
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes($"{payload.Length:x}\r\n"), _stop.Token);
                await stream.WriteAsync(payload, _stop.Token);
                await stream.WriteAsync("\r\n"u8.ToArray(), _stop.Token);
            }

            Interlocked.Add(ref _bytesWritten, payload.Length);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
