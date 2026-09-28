using DAL.Entities;
using ServerServices.Importers.Dedup;

namespace ServerServices.Interfaces;

/// <summary>
/// The deduplication engine (Track 3 milestone 3.3): computes the key that decides whether an
/// imported finding is one the register already holds, and manages the per-scanner configuration
/// that decides how the key is computed.
/// </summary>
public interface IDeduplicationService
{
    /// <summary>
    /// The configuration for an importer, or a synthesised default for one nobody has configured.
    /// Never returns null and never writes on a read.
    /// </summary>
    Task<ScannerDedupConfiguration> GetConfigurationAsync(string importer);

    Task<List<ScannerDedupConfiguration>> GetConfigurationsAsync();

    /// <summary>
    /// Validates and saves a configuration, recording the change in
    /// <c>scanner_dedup_configuration_history</c>. Throws
    /// <see cref="Model.Exceptions.InvalidParameterException"/> for an unknown strategy or hash
    /// field rather than silently dropping it.
    /// </summary>
    Task<ScannerDedupConfiguration> SaveConfigurationAsync(ScannerDedupConfiguration configuration, int? userId);

    Task<List<ScannerDedupConfigurationHistory>> GetConfigurationHistoryAsync(string importer);

    /// <summary>
    /// Every key the configured chain produces for one finding, in chain order. The first is what a
    /// new finding is persisted with; the whole list is what a lookup has to try.
    /// </summary>
    Task<DedupKeyResult> ComputeKeyAsync(DedupContext context, ScannerDedupConfiguration configuration);

    /// <summary>
    /// The same chain, resolved once and reusable — for a caller keying many findings under one
    /// configuration.
    ///
    /// <see cref="ComputeKeyAsync"/> re-resolves the chain on every call, and resolving it
    /// enumerates the plugin directory and loads each enabled plugin. Per finding that is fine; per
    /// finding across half a million of them it is most of the import.
    /// </summary>
    Task<IDedupKeyCalculator> GetKeyCalculatorAsync(ScannerDedupConfiguration configuration);

    /// <summary>
    /// The admin preview (3.3.3): would these two findings be treated as one under the importer's
    /// current configuration? Has no side effects, so a heuristic can be tried before it is saved.
    /// </summary>
    Task<DedupPreview> PreviewAsync(DedupContext left, DedupContext right, string importer);

    /// <summary>Built-in strategies plus any contributed by enabled plugins.</summary>
    Task<List<string>> KnownStrategyNamesAsync();
}

/// <summary>
/// A dedup chain resolved against one scanner configuration, ready to key findings.
///
/// Synchronous by design: every strategy is a pure function, so once the chain is resolved there is
/// nothing left to await, and an async signature here would invite a per-finding await that does no
/// I/O.
/// </summary>
public interface IDedupKeyCalculator
{
    /// <summary>Every key the chain produces for one finding, in chain order.</summary>
    DedupKeyResult ComputeKey(DedupContext context);
}
