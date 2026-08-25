using DAL.Enums;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers;

/// <summary>
/// Maps a tracker kind to its provider (Track 4 milestone 4.2.1). Same shape and the same reasoning
/// as the notification-channel registry: adding a provider is one DI registration.
/// </summary>
public class IssueTrackerProviderRegistry : IIssueTrackerProviderRegistry
{
    private readonly Dictionary<IssueTrackerProviderKind, IIssueTrackerProvider> _byKind;

    public IssueTrackerProviderRegistry(IEnumerable<IIssueTrackerProvider> providers)
    {
        _byKind = new Dictionary<IssueTrackerProviderKind, IIssueTrackerProvider>();

        foreach (var provider in providers)
        {
            if (!_byKind.TryAdd(provider.Kind, provider))
                throw new InvalidOperationException(
                    $"Two issue-tracker providers claim {provider.Kind}: "
                    + $"{_byKind[provider.Kind].GetType().Name} and {provider.GetType().Name}.");
        }

        All = _byKind.Values.OrderBy(p => p.Kind).ToList();
    }

    public IReadOnlyList<IIssueTrackerProvider> All { get; }

    public IIssueTrackerProvider? For(IssueTrackerProviderKind kind) => _byKind.GetValueOrDefault(kind);
}
