using Contracts.Importers;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Importers.Dedup;

/// <summary>
/// Resolves the deduplication key for a finding, and manages the per-scanner configuration that
/// decides how (Track 3 milestone 3.3).
///
/// The engine's contract with the rest of the system: it computes keys and it never merges anything
/// itself. Grouping is the ingestion pipeline's job, and keeping the two apart is what makes the
/// admin preview panel possible — computing a key has no side effects, so a heuristic can be tried
/// against real findings before it is saved.
/// </summary>
public class DeduplicationService : ServiceBase, IDeduplicationService
{
    private readonly IPluginsService _pluginsService;
    private readonly Dictionary<string, IDeduplicationStrategy> _builtIn;

    public DeduplicationService(ILogger logger, IDalService dalService, IPluginsService pluginsService)
        : base(logger, dalService)
    {
        _pluginsService = pluginsService;
        _builtIn = new Dictionary<string, IDeduplicationStrategy>(StringComparer.OrdinalIgnoreCase)
        {
            [UniqueIdFromToolStrategy.StrategyName] = new UniqueIdFromToolStrategy(),
            [HashBasedStrategy.StrategyName] = new HashBasedStrategy(),
            [LegacyHashCodeStrategy.StrategyName] = new LegacyHashCodeStrategy()
        };
    }

    /// <summary>
    /// The default chain for an importer with no saved configuration: the tool's own id first when
    /// it has one, then the field hash. Safe for every importer — <c>UniqueIdFromTool</c> simply
    /// declines for scanners that publish no id.
    /// </summary>
    public const string DefaultStrategyChain =
        UniqueIdFromToolStrategy.StrategyName + "," + HashBasedStrategy.StrategyName;

    public async Task<ScannerDedupConfiguration> GetConfigurationAsync(string importer)
    {
        await using var db = DalService.GetContext();

        var existing = await db.ScannerDedupConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Importer == importer);

        // An importer nobody has configured still needs a chain, and inventing it here rather than
        // inserting a row on first read keeps a read path from writing.
        return existing ?? new ScannerDedupConfiguration
        {
            Importer = importer,
            StrategyChain = DefaultStrategyChain,
            HashFields = string.Join(",", DedupFieldSet.Default),
            AutoCloseMissing = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<List<ScannerDedupConfiguration>> GetConfigurationsAsync()
    {
        await using var db = DalService.GetContext();
        return await db.ScannerDedupConfigurations.AsNoTracking().OrderBy(c => c.Importer).ToListAsync();
    }

    public async Task<ScannerDedupConfiguration> SaveConfigurationAsync(ScannerDedupConfiguration configuration,
        int? userId)
    {
        if (string.IsNullOrWhiteSpace(configuration.Importer))
            throw new InvalidParameterException("importer", "A dedup configuration must name an importer.");

        var chain = ParseChain(configuration.StrategyChain);
        if (chain.Count == 0)
            throw new InvalidParameterException(nameof(configuration.StrategyChain),
                "A dedup configuration must list at least one strategy.");

        // Validated before saving rather than tolerated at import time: an unknown strategy name
        // silently drops out of the chain, and a chain that quietly lost its first strategy changes
        // what counts as the same finding without anyone being told.
        var known = await KnownStrategyNamesAsync();
        var unknown = chain.Where(s => !known.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            throw new InvalidParameterException(nameof(configuration.StrategyChain),
                $"Unknown deduplication strategy: {string.Join(", ", unknown)}. " +
                $"Available: {string.Join(", ", known)}.");

        var fieldSet = DedupFieldSet.Parse(configuration.HashFields);
        if (fieldSet.UnknownFields.Count > 0)
            throw new InvalidParameterException(nameof(configuration.HashFields),
                $"Unknown hash field: {string.Join(", ", fieldSet.UnknownFields)}. " +
                $"Available: {string.Join(", ", DedupFieldSet.Available)}.");

        await using var db = DalService.GetContext();

        var now = DateTime.UtcNow;
        var existing = await db.ScannerDedupConfigurations
            .FirstOrDefaultAsync(c => c.Importer == configuration.Importer);

        var history = new ScannerDedupConfigurationHistory
        {
            Importer = configuration.Importer,
            OldStrategyChain = existing?.StrategyChain,
            NewStrategyChain = string.Join(",", chain),
            OldHashFields = existing?.HashFields,
            NewHashFields = fieldSet.ToString(),
            OldAutoCloseMissing = existing?.AutoCloseMissing,
            NewAutoCloseMissing = configuration.AutoCloseMissing,
            UserId = userId,
            ChangedAt = now
        };

        if (existing == null)
        {
            existing = new ScannerDedupConfiguration
            {
                Importer = configuration.Importer,
                CreatedAt = now
            };
            db.ScannerDedupConfigurations.Add(existing);
        }

        existing.StrategyChain = string.Join(",", chain);
        existing.HashFields = fieldSet.ToString();
        existing.AutoCloseMissing = configuration.AutoCloseMissing;
        existing.UpdatedAt = now;
        existing.UpdatedById = userId;

        db.ScannerDedupConfigurationHistories.Add(history);

        await db.SaveChangesAsync();

        Logger.Information(
            "Dedup configuration for importer {Importer} set to chain {Chain} fields {Fields} autoClose {AutoClose} by user {User}",
            existing.Importer, existing.StrategyChain, existing.HashFields, existing.AutoCloseMissing, userId);

        return existing;
    }

    public async Task<List<ScannerDedupConfigurationHistory>> GetConfigurationHistoryAsync(string importer)
    {
        await using var db = DalService.GetContext();

        return await db.ScannerDedupConfigurationHistories
            .AsNoTracking()
            .Where(h => h.Importer == importer)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync();
    }

    public async Task<DedupKeyResult> ComputeKeyAsync(DedupContext context, ScannerDedupConfiguration configuration)
    {
        var strategies = await ResolveChainAsync(configuration.StrategyChain);
        var fields = DedupFieldSet.Parse(configuration.HashFields);

        var candidates = new List<DedupCandidate>();

        foreach (var strategy in strategies)
        {
            string? key;
            try
            {
                key = strategy.ComputeKey(context, fields);
            }
            catch (Exception ex)
            {
                // A plugin strategy that throws must not fail the import. Skipping it degrades
                // dedup for that finding, which is recoverable; aborting the scan is not.
                Logger.Warning("Deduplication strategy {Strategy} threw for finding {Title}: {Message}",
                    strategy.Name, context.Finding.Title, ex.Message);
                continue;
            }

            if (string.IsNullOrWhiteSpace(key)) continue;

            candidates.Add(new DedupCandidate(strategy.Name, key, strategy.MatchesLegacyImportHash));
        }

        return new DedupKeyResult(candidates);
    }

    public async Task<DedupPreview> PreviewAsync(DedupContext left, DedupContext right, string importer)
    {
        var configuration = await GetConfigurationAsync(importer);

        var leftResult = await ComputeKeyAsync(left, configuration);
        var rightResult = await ComputeKeyAsync(right, configuration);

        // "Would merge" is the same question the ingestion pipeline asks: does any candidate key of
        // one finding appear among the other's? Answering it with the primary keys alone would
        // report a non-merge for two findings that in fact match on a later strategy in the chain.
        var shared = leftResult.Candidates
            .Select(c => c.Key)
            .Intersect(rightResult.Candidates.Select(c => c.Key), StringComparer.Ordinal)
            .ToList();

        return new DedupPreview(configuration, leftResult, rightResult, shared);
    }

    public async Task<List<string>> KnownStrategyNamesAsync()
    {
        var names = _builtIn.Keys.ToList();
        names.AddRange((await PluginStrategiesAsync()).Select(s => s.Name));
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<IDeduplicationStrategy>> ResolveChainAsync(string? chain)
    {
        var names = ParseChain(chain);
        if (names.Count == 0) names = ParseChain(DefaultStrategyChain);

        var plugins = (await PluginStrategiesAsync())
            .ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<IDeduplicationStrategy>();
        foreach (var name in names)
        {
            if (_builtIn.TryGetValue(name, out var builtIn)) resolved.Add(builtIn);
            else if (plugins.TryGetValue(name, out var plugin)) resolved.Add(plugin);
            else
                Logger.Warning(
                    "Deduplication strategy {Strategy} is configured but not available; skipping it for this import",
                    name);
        }

        // A chain that resolved to nothing (every plugin strategy uninstalled, say) would leave
        // findings with no key at all, and a finding with no key duplicates on every import. Fall
        // back to the field hash, which always works.
        if (resolved.Count == 0) resolved.Add(_builtIn[HashBasedStrategy.StrategyName]);

        return resolved;
    }

    private async Task<List<IDeduplicationStrategy>> PluginStrategiesAsync()
    {
        try
        {
            var plugins = await _pluginsService.GetPluginsAsync();
            var strategies = new List<IDeduplicationStrategy>();

            foreach (var info in plugins.Where(p => p.IsEnabled))
            {
                var plugin = await TryLoadAsync(info.Name);
                if (plugin != null) strategies.Add(new PluginDeduplicationStrategy(plugin));
            }

            return strategies;
        }
        catch (Exception ex)
        {
            // Plugin discovery reaches the file system; a broken plugin directory must not stop
            // deduplication, which has perfectly good built-in strategies.
            Logger.Warning("Could not enumerate deduplication plugins: {Message}", ex.Message);
            return [];
        }
    }

    private async Task<IDeduplicationStrategyPlugin?> TryLoadAsync(string name)
    {
        try
        {
            return await _pluginsService.GetPluginAsync<IDeduplicationStrategyPlugin>(name);
        }
        catch (Exception)
        {
            // The plugin exists but is not a dedup strategy. Expected for every other plugin type.
            return null;
        }
    }

    private static List<string> ParseChain(string? chain) =>
        (chain ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}

/// <summary>One strategy's verdict on a finding.</summary>
/// <param name="Strategy">Which strategy produced <paramref name="Key"/>.</param>
/// <param name="Key">The 64-character stored key, or the legacy SHA-1 for the compatibility strategy.</param>
/// <param name="MatchesLegacyImportHash">
/// True when the key should be looked up against <c>import_hash</c> as well as <c>dedup_key</c>.
/// </param>
public record DedupCandidate(string Strategy, string Key, bool MatchesLegacyImportHash);

/// <summary>
/// Every key the chain produced for one finding, in chain order.
///
/// The whole list is kept, not just the winner: the first key is what gets persisted, but a lookup
/// has to try all of them, because a finding imported last month may have been keyed by a strategy
/// that is no longer first in the chain.
/// </summary>
public class DedupKeyResult(IReadOnlyList<DedupCandidate> candidates)
{
    public IReadOnlyList<DedupCandidate> Candidates { get; } = candidates;

    /// <summary>The key persisted on a newly created finding. Null when no strategy had an opinion.</summary>
    public string? PrimaryKey => Candidates.Count > 0 ? Candidates[0].Key : null;

    public string? PrimaryStrategy => Candidates.Count > 0 ? Candidates[0].Strategy : null;

    public bool HasKey => Candidates.Count > 0;
}

/// <summary>
/// The admin preview panel's answer (Track 3 milestone 3.3.3): the two findings' keys and whether
/// the current configuration would treat them as one.
/// </summary>
public record DedupPreview(
    ScannerDedupConfiguration Configuration,
    DedupKeyResult Left,
    DedupKeyResult Right,
    IReadOnlyList<string> SharedKeys)
{
    public bool WouldMerge => SharedKeys.Count > 0;
}
