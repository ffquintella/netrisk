using DnsClient;
using DnsClient.Protocol;

namespace ServerServices.Secrets;

/// <summary>
/// SRV lookups through DnsClient.NET, using the host's configured resolvers.
///
/// A dedicated DNS library rather than <c>System.Net.Dns</c> because the BCL exposes A/AAAA and
/// nothing else — there is no supported way to ask it for an SRV record. The library reads
/// <c>/etc/resolv.conf</c> (or the Windows equivalent) itself, so a NetRisk in a container gets the
/// container's resolver and a NetRisk on a domain-joined host gets the domain's, with no
/// configuration here.
/// </summary>
public class DnsClientSrvLookup : IDnsSrvLookup
{
    private readonly ILookupClient _client;

    public DnsClientSrvLookup()
        : this(new LookupClient(new LookupClientOptions
        {
            // Two seconds and one retry: this lookup sits in front of a credential read, which sits
            // in front of a sync job. A resolver that is not answering should cost seconds, not the
            // five-second default times the number of configured servers.
            Timeout = TimeSpan.FromSeconds(2),
            Retries = 1,
            UseCache = true,
            ThrowDnsErrors = false
        }))
    {
    }

    /// <summary>Test seam.</summary>
    public DnsClientSrvLookup(ILookupClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<DnsSrvRecord>> LookupAsync(string serviceName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        IDnsQueryResponse response;

        try
        {
            response = await _client.QueryAsync(serviceName, QueryType.SRV, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DnsSrvLookupException(
                $"The SRV lookup for '{serviceName}' could not be performed: {ex.Message}", ex);
        }

        // A name with no SRV records is an answer, not a failure: it means "not a cluster", and the
        // caller reports that in terms an operator can act on. A SERVFAIL is a failure, because
        // retrying it later may well work and treating it as "no nodes" would hide an outage.
        if (response.HasError && response.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain)
            throw new DnsSrvLookupException(
                $"The SRV lookup for '{serviceName}' failed: {response.ErrorMessage}");

        return response.Answers.SrvRecords()
            .Select(r => new DnsSrvRecord(r.Target.Value, r.Port, r.Priority, r.Weight,
                (int)r.InitialTimeToLive))
            .ToList();
    }
}
