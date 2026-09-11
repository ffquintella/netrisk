using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
/// <see cref="OutboundHttpRequest.AllowInvalidCertificate"/>, observed against a real TLS server with
/// a certificate nothing trusts.
///
/// Observed rather than asserted on a flag, because the flag is not the behaviour: the option exists
/// so that a vault behind an internal CA can be reached, and the only thing that establishes that is
/// a handshake that fails without it and succeeds with it. A unit test that checked the property was
/// copied onto the request would have passed just as happily against a client that ignored it — which
/// is exactly the class of "documented control that does nothing" this repository has shipped before.
///
/// The server is a socket on 127.0.0.1 speaking one hand-written HTTP response. Loopback is allowed by
/// <see cref="OutboundUrlPolicy"/> by default, so the destination policy is not in the way; a test
/// that needed a public host would be a test of the network.
/// </summary>
[TestSubject(typeof(OutboundHttpClient))]
public class OutboundHttpClientTlsTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static OutboundHttpClient Client() =>
        new(Log, new OutboundUrlPolicy(Log, blockPrivateNetworks: false));

    [Fact]
    public async Task RefusesAnUntrustedCertificateByDefault()
    {
        using var server = new SelfSignedTlsServer();

        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10)
        });

        // A refused handshake is a transport failure, the same shape as an unreachable host: status 0
        // with the reason attached, which is what every caller in the product already handles.
        Assert.Equal(0, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(response.TransportError));
    }

    [Fact]
    public async Task ReachesAnUntrustedCertificateWhenTheRequestAllowsIt()
    {
        using var server = new SelfSignedTlsServer();

        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10),
            AllowInvalidCertificate = true
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("{}", response.Body);
    }

    /// <summary>
    /// The reported reason has to name the certificate problem, not refer the reader to an exception
    /// they cannot see.
    ///
    /// Observed against a real handshake because the useless sentence is produced by
    /// <c>HttpClient</c>, not by this code: a vault connection whose <c>Last test</c> read "The SSL
    /// connection could not be established, see inner exception." told the operator nothing about the
    /// untrusted internal CA that was actually the cause.
    /// </summary>
    [Fact]
    public async Task NamesTheCertificateProblemRatherThanReferringToAnInnerException()
    {
        using var server = new SelfSignedTlsServer();

        using var client = Client();

        var response = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10)
        });

        Assert.Equal(0, response.StatusCode);
        Assert.DoesNotContain("see inner exception", response.TransportError!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("certificate", response.TransportError!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The two clients are separate instances, so relaxing validation for one request must not relax
    /// it for the next one on the same <see cref="OutboundHttpClient"/>. This is the failure mode a
    /// per-request validation callback on one shared handler would have.
    /// </summary>
    [Fact]
    public async Task RelaxingValidationOnceDoesNotRelaxItAfterwards()
    {
        using var server = new SelfSignedTlsServer();

        using var client = Client();

        var allowed = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10),
            AllowInvalidCertificate = true
        });

        var strict = await client.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = server.Url,
            Timeout = TimeSpan.FromSeconds(10)
        });

        Assert.Equal(200, allowed.StatusCode);
        Assert.Equal(0, strict.StatusCode);
    }

    /// <summary>
    /// A TLS listener with a certificate no store contains, answering every connection with the same
    /// minimal HTTP/1.1 response.
    /// </summary>
    private sealed class SelfSignedTlsServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly CancellationTokenSource _stop = new();

        public SelfSignedTlsServer()
        {
            using var key = RSA.Create(2048);

            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());

            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1));

            // Round-tripped through a PKCS#12 blob because a certificate created with an ephemeral key
            // cannot be used by SslStream on macOS — the handshake fails with an unrelated message.
            _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            Url = $"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

            _ = Task.Run(AcceptAsync);
        }

        public string Url { get; }

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            await using var tls = new SslStream(client.GetStream(), false);
                            await tls.AuthenticateAsServerAsync(_certificate);

                            var buffer = new byte[4096];
                            await tls.ReadAsync(buffer, _stop.Token);

                            var body = "{}"u8.ToArray();
                            var head = Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                                + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");

                            await tls.WriteAsync(head, _stop.Token);
                            await tls.WriteAsync(body, _stop.Token);
                            await tls.FlushAsync(_stop.Token);
                        }
                        catch (Exception)
                        {
                            // A client that walked away mid-handshake is the point of one of these
                            // tests, not a failure of the server.
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _certificate.Dispose();
            _stop.Dispose();
        }
    }
}
