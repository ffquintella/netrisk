using System.Collections.Concurrent;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// The budget of derived computations (S40 §8.1, §11.8): at most <see cref="RiskIndexDerivedBudget.PerUser"/> at once per
/// user and <see cref="RiskIndexDerivedBudget.PerProcess"/> per process, each within a time limit. Over the limit:
/// <c>503 derived_computation_busy</c> with <c>Retry-After</c>, no queueing; over the time: <c>503 derived_computation_timeout</c>.
/// A timed-out computation keeps its slots until it really ends, so slow requests cannot pile up behind released slots.
/// </summary>
public interface IRiskIndexDerivedBudget
{
    Task<T> RunAsync<T>(int userId, Func<CancellationToken, Task<T>> compute, CancellationToken cancellationToken = default);
}

public sealed class RiskIndexDerivedBudget : IRiskIndexDerivedBudget
{
    public const int DefaultPerUser = 2;
    public const int DefaultPerProcess = 4;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _process;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _users = new();

    public int PerUser { get; }
    public int PerProcess { get; }
    public TimeSpan Timeout { get; }

    public RiskIndexDerivedBudget() : this(DefaultPerUser, DefaultPerProcess, DefaultTimeout)
    {
    }

    public RiskIndexDerivedBudget(int perUser, int perProcess, TimeSpan timeout)
    {
        PerUser = perUser;
        PerProcess = perProcess;
        Timeout = timeout;
        _process = new SemaphoreSlim(Math.Max(0, perProcess), Math.Max(1, perProcess));
    }

    public async Task<T> RunAsync<T>(int userId, Func<CancellationToken, Task<T>> compute, CancellationToken cancellationToken = default)
    {
        var user = _users.GetOrAdd(userId, _ => new SemaphoreSlim(Math.Max(0, PerUser), Math.Max(1, PerUser)));
        if (!user.Wait(0))
            throw RiskIndexException.Unavailable(RiskIndexErrors.DerivedBusy,
                "Too many derived computations of yours are running; try again shortly.", RetryAfter);
        if (!_process.Wait(0))
        {
            user.Release();
            throw RiskIndexException.Unavailable(RiskIndexErrors.DerivedBusy,
                "The server is computing other derived views; try again shortly.", RetryAfter);
        }

        var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Timeout);
        var token = limit.Token;

        // The slots go back when the work really ends, not when the caller stops waiting for it.
        var work = Task.Run(async () =>
        {
            try
            {
                return await compute(token);
            }
            finally
            {
                limit.Dispose();
                _process.Release();
                user.Release();
            }
        }, CancellationToken.None);

        try
        {
            return await work.WaitAsync(Timeout, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException ||
                                   (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw RiskIndexException.Unavailable(RiskIndexErrors.DerivedTimeout,
                "The derived computation took longer than its budget; try a narrower node.", RetryAfter);
        }
    }
}
