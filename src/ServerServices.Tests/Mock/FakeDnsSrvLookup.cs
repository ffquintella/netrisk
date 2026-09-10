using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ServerServices.Secrets;

namespace ServerServices.Tests.Mock;

/// <summary>
/// The SRV resolver vault-discovery tests answer with.
///
/// A real lookup would make these tests depend on the DNS of whatever machine runs them: a name that
/// does not exist costs a resolver timeout to fail, and one that does gives a different answer in CI
/// than at a desk. It also records what was asked, which is how a test asserts that a bare cluster
/// name was turned into <c>_bvault._tcp.&lt;name&gt;</c> rather than queried as typed.
/// </summary>
public class FakeDnsSrvLookup : IDnsSrvLookup
{
    /// <summary>Every service name queried, in order.</summary>
    public List<string> Queries { get; } = new();

    private readonly Dictionary<string, IReadOnlyList<DnsSrvRecord>> _answers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set when the resolver should be treated as unreachable rather than as answering.</summary>
    public string? FailWith { get; set; }

    public FakeDnsSrvLookup With(string serviceName, params DnsSrvRecord[] records)
    {
        _answers[serviceName] = records;
        return this;
    }

    /// <summary>One record, with the priority/weight/TTL most tests do not care about.</summary>
    public FakeDnsSrvLookup With(string serviceName, string target, int port,
        int priority = 10, int weight = 10, int ttl = 30) =>
        With(serviceName, new DnsSrvRecord(target, port, priority, weight, ttl));

    public Task<IReadOnlyList<DnsSrvRecord>> LookupAsync(string serviceName,
        CancellationToken ct = default)
    {
        Queries.Add(serviceName);

        if (FailWith != null) throw new DnsSrvLookupException(FailWith);

        return Task.FromResult(_answers.TryGetValue(serviceName, out var records)
            ? records
            : (IReadOnlyList<DnsSrvRecord>) Array.Empty<DnsSrvRecord>());
    }
}
