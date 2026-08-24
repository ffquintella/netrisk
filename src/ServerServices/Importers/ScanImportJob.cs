using Contracts.Importers;
using DAL.Entities;
using Serilog;
using ServerServices.Events;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Importers;

/// <summary>
/// Runs one scanner import as a background job (Track 3 milestone 3.1.4).
///
/// Imports are asynchronous by default because they are not fast: a 500 MB Nessus file with 100k
/// findings is a normal thing for a customer to upload, and a synchronous endpoint for it is a
/// timeout waiting to happen. The job reports progress and honours cancellation through the same
/// <see cref="IJobRunner"/> machinery every other long operation uses, so the existing job UI works
/// unchanged.
/// </summary>
public class ScanImportJob : IJobRunner
{
    private readonly IImporterRegistry _registry;
    private readonly IFindingIngestionService _ingestion;
    private readonly IJobManager _jobManager;
    private readonly ILogger _logger;

    private readonly string _importerName;
    private readonly Func<Stream> _openReport;
    private readonly ImportContext _context;
    private readonly ImportIngestionRequest _request;

    private int _jobId;

    /// <summary>
    /// The <c>scan_imports</c> row this job is filling in. Set before the job starts so the caller
    /// can hand a client something to poll immediately.
    /// </summary>
    public int ImportId { get; private set; }

    public ScanImportJob(ILogger logger, IImporterRegistry registry, IFindingIngestionService ingestion,
        IJobManager jobManager, string importerName, Func<Stream> openReport, ImportContext context,
        ImportIngestionRequest request, User? user)
    {
        _logger = logger;
        _registry = registry;
        _ingestion = ingestion;
        _jobManager = jobManager;
        _importerName = importerName;
        _openReport = openReport;
        _context = context;
        _request = request;
        LoggedUser = user;
    }

    public string JobName => $"{_importerName} import";

    public CancellationTokenSource CancellationTokenSource { get; } = new();

    public User? LoggedUser { get; set; }

    public event EventHandler<JobEventArgs>? StepCompleted;
    public event EventHandler<JobEventArgs>? JobEnded;
    public event EventHandler<JobEventArgs>? JobFailed;

    /// <summary>
    /// True when the caller's idempotency key had already been used, so no work was started and
    /// <see cref="ImportId"/> refers to the original import.
    /// </summary>
    public bool IsReplay { get; private set; }

    /// <summary>
    /// Reserves the <c>scan_imports</c> row, then hands the job to the job manager. Returns the job
    /// id (0 for a replay); <see cref="ImportId"/> carries the import row's id either way.
    /// </summary>
    public async Task<int> StartAsync()
    {
        var reservation = await _ingestion.BeginImportAsync(_request);

        ImportId = reservation.Import.Id;
        IsReplay = reservation.IsReplay;

        // A repeated idempotency key resolves to the original import. Nothing to run: the caller
        // reads that row and gets the original counts, which is the whole point of the header.
        if (IsReplay) return 0;

        _jobId = await _jobManager.RunAndRegisterJob(this);
        return _jobId;
    }

    public async Task Run()
    {
        try
        {
            Progress(5);

            await using var report = _openReport();

            var importer = await _registry.ResolveOrDetectAsync(_importerName, report, _context.FileName);

            // Detection reads a prefix; the parse has to start from the beginning.
            if (report.CanSeek) report.Position = 0;

            Progress(10);

            var parsed = await importer.ImportAsync(report, _context, CancellationTokenSource.Token);

            // Parsing is the slow part for big XML reports and persisting is the slow part for big
            // result sets, so the two are reported as roughly equal halves rather than pretending
            // to know more than we do.
            Progress(50);

            _request.ExistingImportId = ImportId;
            var result = await _ingestion.IngestAsync(parsed, _request, CancellationTokenSource.Token);

            Progress(100);

            Complete($"Imported {result.NewCount} new, updated {result.UpdatedCount}, " +
                     $"suppressed {result.DuplicateCount}, closed {result.ClosedCount}, " +
                     $"skipped {result.SkippedCount} findings");
        }
        catch (OperationCanceledException)
        {
            await _ingestion.FailImportAsync(ImportId, "Import cancelled by the user.");
            Fail("Import cancelled");
        }
        catch (Exception ex)
        {
            var message = ex.InnerException == null ? ex.Message : $"{ex.Message} - {ex.InnerException.Message}";

            // The failure is recorded on the import row as well as the job, because that is what a
            // CI runner polling GET /import-jobs/{id} reads.
            await _ingestion.FailImportAsync(ImportId, message);

            _logger.Error(ex, "Import {Import} using {Importer} failed", ImportId, _importerName);
            Fail(message);
        }
    }

    public void Cancel() => CancellationTokenSource.Cancel();

    /// <summary>
    /// Part of <see cref="IJobRunner"/>. Named to match the interface's vocabulary; the private
    /// helpers below are what the job body actually calls.
    /// </summary>
    public void Error(string message) => Fail(message);

    public void RegisterProgress(int progress) => Progress(progress);

    public void RegisterResult(string result) => Complete(result);

    private void Progress(int percent) =>
        StepCompleted?.Invoke(this, new JobEventArgs { JobId = _jobId, PercentCompleted = percent });

    private void Complete(string message) =>
        JobEnded?.Invoke(this, new JobEventArgs { JobId = _jobId, PercentCompleted = 100, Message = message });

    private void Fail(string message) =>
        JobFailed?.Invoke(this, new JobEventArgs { JobId = _jobId, PercentCompleted = 100, Message = message });
}
