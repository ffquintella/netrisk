using DAL.Enums;
using ServerServices.Interfaces;

namespace ServerServices.Notifications;

/// <summary>
/// Maps a channel kind to its provider (Track 4 milestone 4.1.1).
///
/// Built from whatever <see cref="INotificationChannel"/> implementations DI supplies, so registering
/// a provider is one line in the bootstrapper and nothing else. A duplicate kind is a configuration
/// error worth failing on at startup rather than resolving arbitrarily at send time.
/// </summary>
public class NotificationChannelRegistry : INotificationChannelRegistry
{
    private readonly Dictionary<NotificationChannelKind, INotificationChannel> _byKind;

    public NotificationChannelRegistry(IEnumerable<INotificationChannel> channels)
    {
        _byKind = new Dictionary<NotificationChannelKind, INotificationChannel>();

        foreach (var channel in channels)
        {
            if (!_byKind.TryAdd(channel.Kind, channel))
                throw new InvalidOperationException(
                    $"Two notification providers claim {channel.Kind}: "
                    + $"{_byKind[channel.Kind].GetType().Name} and {channel.GetType().Name}.");
        }

        All = _byKind.Values.OrderBy(c => c.Kind).ToList();
    }

    public IReadOnlyList<INotificationChannel> All { get; }

    public INotificationChannel? For(NotificationChannelKind kind) =>
        _byKind.GetValueOrDefault(kind);
}
