using DAL.Entities;
using DAL.Enums;
using Model.Notifications;

namespace ServerServices.Interfaces;

/// <summary>
/// A delivery provider (Track 4 milestone 4.1.1).
///
/// Implementations render a <see cref="NotificationMessage"/> natively — Block Kit, Adaptive Card,
/// HTML mail, documented JSON — and are otherwise stateless: retry, fallback, digesting and the
/// delivery log all live in the dispatcher, so adding a fifth provider means writing one renderer
/// and nothing else.
/// </summary>
public interface INotificationChannel
{
    /// <summary>Which channel kind this provider serves. One provider per kind.</summary>
    NotificationChannelKind Kind { get; }

    /// <summary>Stable name for logs and for the admin UI's provider list.</summary>
    string Name { get; }

    /// <summary>
    /// Delivers <paramref name="message"/> using <paramref name="configuration"/> — already
    /// decrypted by the dispatcher, so a provider never sees ciphertext and never needs the key.
    /// </summary>
    Task<DeliveryResult> SendAsync(NotificationMessage message, ChannelConfiguration configuration,
        CancellationToken ct = default);

    /// <summary>
    /// The admin UI's "send test message". Deliberately a real send rather than a reachability
    /// probe: a webhook URL that resolves but posts into the wrong workspace passes a ping and fails
    /// the only test that matters.
    /// </summary>
    Task<ChannelTestResult> TestAsync(ChannelConfiguration configuration, CancellationToken ct = default);
}

/// <summary>
/// Resolves the provider for a channel row (Track 4 milestone 4.1.1).
///
/// A registry rather than a switch in the dispatcher so a plugin-supplied provider could register
/// itself later without the dispatcher changing.
/// </summary>
public interface INotificationChannelRegistry
{
    /// <summary>Every registered provider, for the admin UI's channel-kind picker.</summary>
    IReadOnlyList<INotificationChannel> All { get; }

    /// <summary>The provider for a kind, or null when none is registered.</summary>
    INotificationChannel? For(NotificationChannelKind kind);
}
