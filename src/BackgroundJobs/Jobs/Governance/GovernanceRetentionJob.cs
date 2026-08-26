using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// Keeps the two governance tables that grow without bound bounded (Track 8 milestone 8.4.1 and
/// security finding NR-2026-028).
///
/// The field-level audit trail is trimmed to the configured retention window, and revoked-token rows
/// are dropped once the token they revoke has expired anyway. A retention policy that is documented
/// and not implemented is worse than none — it tells an operator the data is gone when it is not —
/// and a revocation list nothing prunes is the usual reason one gets abandoned.
/// </summary>
public class GovernanceRetentionJob(
    ILogger logger,
    DalService dalService,
    IAuditTrailService auditTrail,
    ITokenRevocationService revocation)
    : BaseJob(logger, dalService), IJob
{
    public void Run() => RunAsync().GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var auditRows = await auditTrail.ApplyRetentionAsync(now);
        var tokens = await revocation.PruneExpiredAsync(now);

        if (auditRows == 0 && tokens == 0)
            Log.Debug("Governance retention pass found nothing to remove");
        else
            Log.Information("Governance retention removed {Audit} audit row(s) and {Tokens} expired " +
                            "revocation row(s)", auditRows, tokens);
    }
}
