using System.Threading;

namespace GUIClient.Tools.Track9;

public sealed class Track9RegistersLoadGate
{
    private long _generation;

    public long Begin() => Interlocked.Increment(ref _generation);
    public bool IsCurrent(long token) => token == Volatile.Read(ref _generation);
    public void DiscardOutstanding() => Interlocked.Increment(ref _generation);
}
