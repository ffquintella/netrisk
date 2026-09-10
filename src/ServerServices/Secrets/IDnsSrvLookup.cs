namespace ServerServices.Secrets;

/// <summary>One SRV record, reduced to the four fields that decide which node to talk to.</summary>
/// <param name="Target">The node's host name, possibly fully qualified with a trailing dot.</param>
/// <param name="Port">The port the node serves on.</param>
/// <param name="Priority">Lower is preferred. RFC 2782 orders strictly by this.</param>
/// <param name="Weight">Relative share among records of equal priority.</param>
/// <param name="TimeToLiveSeconds">The record's TTL, which bounds how long a choice may be reused.</param>
public readonly record struct DnsSrvRecord(
    string Target,
    int Port,
    int Priority,
    int Weight,
    int TimeToLiveSeconds);

/// <summary>
/// SRV lookups, behind an interface so that resolution is testable.
///
/// It exists because the alternative is a unit test that depends on the resolver of whatever machine
/// runs it: a real query for a name that does not exist takes a DNS timeout to fail, and one for a
/// name that does exist gives a different answer at the office and in CI. <c>System.Net.Dns</c>
/// cannot do SRV at all, so this is also the only seam where the DNS library is named.
/// </summary>
public interface IDnsSrvLookup
{
    /// <summary>
    /// The SRV records for <paramref name="serviceName"/>, in no particular order — ordering is
    /// RFC 2782's job and belongs to the caller. Empty when the name has none; implementations must
    /// not throw for NXDOMAIN, which is an answer.
    /// </summary>
    /// <exception cref="DnsSrvLookupException">The resolver could not be reached or refused.</exception>
    Task<IReadOnlyList<DnsSrvRecord>> LookupAsync(string serviceName, CancellationToken ct = default);
}

/// <summary>A DNS lookup that failed for a reason other than "the name has no records".</summary>
public class DnsSrvLookupException : Exception
{
    public DnsSrvLookupException(string message) : base(message) { }

    public DnsSrvLookupException(string message, Exception inner) : base(message, inner) { }
}
