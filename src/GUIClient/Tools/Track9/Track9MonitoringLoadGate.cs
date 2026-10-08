using System.Threading;

namespace GUIClient.Tools.Track9;

/// <summary>Version token used by Track 9 workspaces to reject results from superseded asynchronous reads.</summary>
public sealed class Track9MonitoringLoadGate
{
    private long _generation;

    public long Begin() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long token) => token == Volatile.Read(ref _generation);

    public void DiscardOutstanding() => Interlocked.Increment(ref _generation);
}
