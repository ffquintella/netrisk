namespace DAL.Enums;

/// <summary>
/// Lifecycle of a scan import (Track 3 milestone 3.1.4), persisted in <c>scan_imports.status</c>.
///
/// A small closed set rather than a reuse of <c>Model.IntStatus</c>: a CI runner polls this and
/// branches on it, so the value set has to be one a caller can enumerate and rely on.
/// </summary>
public enum ScanImportStatus
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5
}
