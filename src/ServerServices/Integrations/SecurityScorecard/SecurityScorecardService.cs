using Contracts.Importers;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Integrations.SecurityScorecard;

/// <summary>
/// SecurityScorecard synchronization (Track 4 milestone 4.5).
///
/// Two mappings deserve to be stated because they are easy to get backwards. SecurityScorecard's score
/// is 0–100 where *higher is better*, and NetRisk's Cyber Risk Index is 0–100 where higher is worse —
/// so the index is <c>100 - score</c>, and a company with an A rating gets a low index rather than a
/// high one. And the domain has no host of its own in NetRisk, so findings are attached to a synthetic
/// "domain asset" host, which is what gives them somewhere to live in a register organized by asset.
/// </summary>
public class SecurityScorecardService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    ISecurityScorecardClient client,
    IFindingIngestionService ingestion)
    : ServiceBase(logger, dalService), ISecurityScorecardService
{
    /// <summary>Importer name the findings are recorded under, and their dedup identity.</summary>
    public const string ImporterName = "securityscorecard";

    public const string ProviderName = "SecurityScorecard";

    /// <summary>The custom category active issues are filed under, per the milestone.</summary>
    public const string IssueCategory = "SecurityScorecard_Issue";

    // --- connections ------------------------------------------------------------------------

    public async Task<List<SecurityScorecardConnectionView>> GetConnectionsAsync(bool includeDisabled = true)
    {
        await using var db = DalService.GetContext();

        var connections = await db.SecurityScorecardConnections
            .Where(c => includeDisabled || c.Enabled)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return connections.Select(ToView).ToList();
    }

    public async Task<SecurityScorecardConnectionView> GetConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();
        return ToView(await LoadAsync(db, id));
    }

    public async Task<SecurityScorecardConnectionView> CreateConnectionAsync(
        SecurityScorecardConnection connection, string? apiToken)
    {
        Validate(connection);

        await using var db = DalService.GetContext();

        if (await db.SecurityScorecardConnections.AnyAsync(c => c.Name == connection.Name))
            throw new InvalidParameterException(nameof(connection.Name),
                $"A SecurityScorecard connection named '{connection.Name}' already exists.");

        var stored = Copy(connection, new SecurityScorecardConnection { CreatedAt = DateTime.UtcNow });
        stored.EncryptedApiToken = protector.Protect(apiToken);

        db.SecurityScorecardConnections.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("SecurityScorecard connection {Name} created for domain {Domain}",
            stored.Name, stored.Domain);

        return ToView(stored);
    }

    public async Task<SecurityScorecardConnectionView> UpdateConnectionAsync(
        SecurityScorecardConnection connection, string? apiToken)
    {
        Validate(connection);

        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, connection.Id);

        if (await db.SecurityScorecardConnections.AnyAsync(c => c.Name == connection.Name
                                                               && c.Id != connection.Id))
            throw new InvalidParameterException(nameof(connection.Name),
                $"A SecurityScorecard connection named '{connection.Name}' already exists.");

        Copy(connection, stored);
        stored.UpdatedAt = DateTime.UtcNow;

        if (apiToken != null) stored.EncryptedApiToken = protector.Protect(apiToken);

        await db.SaveChangesAsync();

        return ToView(stored);
    }

    public async Task DeleteConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);

        db.SecurityScorecardConnections.Remove(stored);
        await db.SaveChangesAsync();

        Logger.Information("SecurityScorecard connection {Id} ({Name}) deleted", id, stored.Name);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, id);

        try
        {
            return await client.TestAsync(connection, protector.Unprotect(connection.EncryptedApiToken));
        }
        catch (SecretProtectionException ex)
        {
            return ConnectionTestResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Warning("Testing SecurityScorecard connection {Id} threw: {Message}", id, ex.Message);
            return ConnectionTestResult.Fail($"The test failed: {ex.Message}");
        }
    }

    // --- synchronization --------------------------------------------------------------------

    public async Task<PostureSyncResult> SyncAsync(int connectionId, CancellationToken ct = default)
    {
        var result = new PostureSyncResult();

        SecurityScorecardConnection connection;

        await using (var db = DalService.GetContext())
        {
            connection = await LoadAsync(db, connectionId);
        }

        var log = await BeginLogAsync(connection);
        var token = protector.Unprotect(connection.EncryptedApiToken);

        try
        {
            // 4.5.2 — overall score and grade, then the ten factors.
            var company = await client.GetCompanyAsync(connection, token, ct);
            var factors = await client.GetFactorsAsync(connection, token, ct);

            await SyncPostureAsync(connection, company, factors, result, ct);

            // 4.5.3 — CVEs and active issues, both ingested as findings against the domain asset.
            var issues = new List<SecurityScorecardIssue>();

            if (connection.SyncVulnerabilities)
                issues.AddRange(await client.GetVulnerabilitiesAsync(connection, token, ct));

            if (connection.SyncIssues)
                issues.AddRange(await client.GetIssuesAsync(connection, token, ct));

            if (issues.Count > 0) await IngestIssuesAsync(connection, issues, result, ct);

            await CompleteLogAsync(log, connection, result, null);
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.Messages.Add(ex.Message);

            Logger.Error(ex, "SecurityScorecard sync for connection {Connection} failed", connection.Name);

            await CompleteLogAsync(log, connection, result, ex.Message);
        }

        return result;
    }

    public async Task<PostureSyncResult> SyncDueConnectionsAsync(DateTime nowUtc,
        CancellationToken ct = default)
    {
        var combined = new PostureSyncResult();

        List<int> due;

        await using (var db = DalService.GetContext())
        {
            var connections = await db.SecurityScorecardConnections
                .Where(c => c.Enabled)
                .Select(c => new { c.Id, c.LastSyncAt, c.SyncIntervalHours })
                .ToListAsync(ct);

            due = connections
                .Where(c => c.LastSyncAt == null
                            || c.LastSyncAt.Value.AddHours(Math.Max(1, c.SyncIntervalHours)) <= nowUtc)
                .Select(c => c.Id)
                .ToList();
        }

        foreach (var connectionId in due)
        {
            var result = await SyncAsync(connectionId, ct);

            combined.HostsCreated += result.HostsCreated;
            combined.HostsUpdated += result.HostsUpdated;
            combined.FindingsCreated += result.FindingsCreated;
            combined.FindingsUpdated += result.FindingsUpdated;
            combined.PostureRowsWritten += result.PostureRowsWritten;
            combined.Errors += result.Errors;
            combined.Messages.AddRange(result.Messages);
            combined.CyberRiskIndex ??= result.CyberRiskIndex;
        }

        return combined;
    }

    /// <summary>
    /// Appends this run's factor scores to the history and writes the entity's Cyber Risk Index
    /// (4.5.2).
    ///
    /// Append-only, one row per factor per run. Overwriting yesterday's score would leave the product
    /// knowing the current Patching Cadence and nothing about whether it is getting worse, which is the
    /// only question a factor score can usefully answer.
    /// </summary>
    private async Task SyncPostureAsync(SecurityScorecardConnection connection,
        SecurityScorecardCompany? company, List<SecurityScorecardFactorScore> factors,
        PostureSyncResult result, CancellationToken ct)
    {
        var capturedAt = DateTime.UtcNow;

        await using var db = DalService.GetContext();

        foreach (var factor in factors)
        {
            db.SecurityScorecardFactors.Add(new SecurityScorecardFactor
            {
                ConnectionId = connection.Id,
                EntityId = connection.EntityId,
                FactorName = factor.Name,
                Score = factor.Score,
                Grade = factor.Grade,
                IssueCount = factor.IssueCount,
                IsOverall = false,
                CapturedAt = capturedAt
            });

            result.PostureRowsWritten++;
        }

        if (company?.Score != null)
        {
            // The overall score rides in the same table with a flag rather than in its own, so the whole
            // posture history is one ordered query instead of a join across two shapes.
            db.SecurityScorecardFactors.Add(new SecurityScorecardFactor
            {
                ConnectionId = connection.Id,
                EntityId = connection.EntityId,
                FactorName = "overall",
                Score = company.Score.Value,
                Grade = company.Grade,
                IsOverall = true,
                CapturedAt = capturedAt
            });

            result.PostureRowsWritten++;

            // SecurityScorecard: higher is better. Cyber Risk Index: higher is worse. Inverting here is
            // the whole reason this line has a comment.
            var index = (double)(100 - company.Score.Value);

            result.CyberRiskIndex = index;

            if (connection.EntityId != null)
            {
                var entity = await db.Entities.FirstOrDefaultAsync(e => e.Id == connection.EntityId, ct);

                if (entity != null)
                {
                    entity.CyberRiskIndex = index;
                    entity.PostureGrade = company.Grade;
                    entity.PostureSource = ProviderName;
                    entity.PostureUpdatedAt = capturedAt;
                }
            }

            Logger.Information(
                "SecurityScorecard {Domain} scored {Score} ({Grade}); Cyber Risk Index set to {Index}",
                connection.Domain, company.Score, company.Grade, index);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ingests CVEs and active issues as findings against the domain asset (4.5.3), through the shared
    /// ingestion pipeline so they get dedup, sticky triage and SLA due dates like any other import.
    /// </summary>
    private async Task IngestIssuesAsync(SecurityScorecardConnection connection,
        List<SecurityScorecardIssue> issues, PostureSyncResult result, CancellationToken ct)
    {
        var domainHost = await EnsureDomainHostAsync(connection, result, ct);

        var parsed = new ImportResult
        {
            DetectedTool = ImporterName,
            // Full scan: SecurityScorecard's issue list is the complete current state for the domain, so
            // an issue that has dropped off it genuinely has been resolved and may be auto-closed.
            IsFullScan = true,
            ScanDate = DateTime.UtcNow
        };

        foreach (var issue in issues)
        {
            var target = string.IsNullOrWhiteSpace(issue.Target) ? connection.Domain : issue.Target!;

            var finding = new NormalizedFinding
            {
                Tool = ImporterName,
                // Type plus target plus CVE is the identity: the same issue on the same host is the same
                // finding, and the same issue on a different subdomain is not.
                ToolUniqueId = $"{issue.Type}:{target}:{issue.CveId ?? "-"}",
                RuleId = issue.CveId ?? issue.Type,
                Title = TitleFor(issue),
                Description = issue.Description,
                Severity = MapSeverity(issue),
                RawSeverity = issue.Severity,
                Cvss3BaseScore = issue.CvssScore,
                FirstSeen = issue.FirstSeen,
                LastSeen = issue.LastSeen,
                Location = issue.Port == null ? target : $"{target}:{issue.Port}",
                Host = new NormalizedHost
                {
                    HostName = domainHost.HostName,
                    Fqdn = domainHost.Fqdn,
                    Port = issue.Port,
                    ServiceName = issue.Port == null ? null : issue.Type
                },
                Evidence = BuildEvidence(connection, issue)
            };

            if (!string.IsNullOrWhiteSpace(issue.CveId)) finding.Cves.Add(issue.CveId!);

            // The custom category the milestone asks for, carried as a tool field so it survives into
            // the finding without needing a schema change.
            finding.ToolFields["category"] = issue.IsVulnerability
                ? "SecurityScorecard_Vulnerability"
                : IssueCategory;

            finding.ToolFields["issueType"] = issue.Type;

            if (issue.FactorName != null) finding.ToolFields["factor"] = issue.FactorName;

            parsed.Findings.Add(finding);
        }

        var import = await ingestion.IngestAsync(parsed, new ImportIngestionRequest
        {
            Importer = ImporterName,
            FileName = $"SecurityScorecard {connection.Domain} {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            EntityId = connection.EntityId
        }, ct);

        result.ImportId = import.Id;
        result.FindingsCreated += import.NewCount;
        result.FindingsUpdated += import.UpdatedCount;
    }

    /// <summary>
    /// Finds or creates the synthetic host that stands for the rated domain.
    ///
    /// SecurityScorecard rates a domain, not a machine, and NetRisk's register is organized by asset. A
    /// single domain asset gives those findings somewhere coherent to live; attaching them to no host at
    /// all would leave them invisible in every asset-oriented view.
    /// </summary>
    private async Task<Host> EnsureDomainHostAsync(SecurityScorecardConnection connection,
        PostureSyncResult result, CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        var externalId = connection.Domain.ToLowerInvariant();

        var host = await db.Hosts.FirstOrDefaultAsync(
            h => h.ExternalProvider == ProviderName && h.ExternalId == externalId, ct);

        if (host != null)
        {
            host.LastVerificationDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            result.HostsUpdated++;
            return host;
        }

        host = new Host
        {
            HostName = connection.Domain,
            Fqdn = connection.Domain,
            Source = ProviderName,
            Status = 1,
            RegistrationDate = DateTime.UtcNow,
            EntityId = connection.EntityId,
            ExternalId = externalId,
            ExternalProvider = ProviderName,
            Comment = "Domain asset created by the SecurityScorecard integration. Findings rated against "
                      + "the domain rather than a specific machine are attached here.",
            Os = "n/a"
        };

        db.Hosts.Add(host);
        await db.SaveChangesAsync(ct);

        result.HostsCreated++;

        return host;
    }

    public async Task<List<SecurityScorecardFactor>> GetFactorHistoryAsync(int connectionId, int limit = 500)
    {
        await using var db = DalService.GetContext();

        return await db.SecurityScorecardFactors
            .Where(f => f.ConnectionId == connectionId)
            .OrderByDescending(f => f.CapturedAt)
            .ThenBy(f => f.FactorName)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToListAsync();
    }

    public async Task<List<IntegrationSyncLog>> GetSyncLogAsync(int limit = 50)
    {
        await using var db = DalService.GetContext();

        return await db.IntegrationSyncLogs
            .Where(l => l.Integration == IntegrationKind.SecurityScorecard)
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// A readable title. SecurityScorecard's issue types are machine names
    /// (<c>spf_record_missing</c>), and a register full of those is unreadable.
    /// </summary>
    internal static string TitleFor(SecurityScorecardIssue issue)
    {
        var name = SecurityScorecardFactors.Humanize(issue.Type);

        if (!string.IsNullOrWhiteSpace(issue.CveId))
            return string.IsNullOrWhiteSpace(issue.Target)
                ? $"{issue.CveId}"
                : $"{issue.CveId} on {issue.Target}";

        return string.IsNullOrWhiteSpace(issue.Target) ? name : $"{name} — {issue.Target}";
    }

    private static string BuildEvidence(SecurityScorecardConnection connection, SecurityScorecardIssue issue)
    {
        var lines = new List<string>
        {
            $"Reported by SecurityScorecard for domain {connection.Domain}.",
            $"Issue type: {issue.Type}"
        };

        if (issue.FactorName != null)
            lines.Add($"Risk factor: {SecurityScorecardFactors.Humanize(issue.FactorName)}");

        if (issue.Target != null) lines.Add($"Target: {issue.Target}");
        if (issue.Port != null) lines.Add($"Port: {issue.Port}");
        if (issue.FirstSeen != null) lines.Add($"First seen: {issue.FirstSeen:yyyy-MM-dd}");
        if (issue.LastSeen != null) lines.Add($"Last seen: {issue.LastSeen:yyyy-MM-dd}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Maps SecurityScorecard's severity words onto NetRisk's scale, falling back to CVSS.
    ///
    /// Their <c>info</c> is genuinely informational, but their <c>positive</c> means "this is good news"
    /// — a finding NetRisk should not carry at all, so it maps to None and the ingestion pipeline's
    /// negligible filter drops it.
    /// </summary>
    internal static NormalizedSeverity MapSeverity(SecurityScorecardIssue issue)
    {
        var word = (issue.Severity ?? string.Empty).Trim().ToLowerInvariant();

        return word switch
        {
            "critical" => NormalizedSeverity.Critical,
            "high" => NormalizedSeverity.High,
            "medium" or "moderate" => NormalizedSeverity.Medium,
            "low" => NormalizedSeverity.Low,
            "info" or "informational" or "positive" => NormalizedSeverity.None,
            _ => issue.CvssScore switch
            {
                null => issue.IsVulnerability ? NormalizedSeverity.Medium : NormalizedSeverity.Low,
                >= 9.0 => NormalizedSeverity.Critical,
                >= 7.0 => NormalizedSeverity.High,
                >= 4.0 => NormalizedSeverity.Medium,
                > 0 => NormalizedSeverity.Low,
                _ => NormalizedSeverity.None
            }
        };
    }

    private void Validate(SecurityScorecardConnection connection)
    {
        if (connection == null)
            throw new InvalidParameterException(nameof(connection), "A connection is required.");

        if (string.IsNullOrWhiteSpace(connection.Name))
            throw new InvalidParameterException(nameof(connection.Name), "A connection requires a name.");

        if (string.IsNullOrWhiteSpace(connection.Domain))
            throw new InvalidParameterException(nameof(connection.Domain),
                "A target domain is required (for example acme.com).");

        var domain = connection.Domain.Trim().ToLowerInvariant();

        // A URL or an email address here is the common mistake, and it produces a 404 from
        // SecurityScorecard that reads as "no scorecard exists" rather than "you typed a URL".
        if (domain.Contains("://") || domain.Contains('/') || domain.Contains('@') || !domain.Contains('.'))
            throw new InvalidParameterException(nameof(connection.Domain),
                "The domain must be a bare registered domain such as acme.com — not a URL, a path or an "
                + "email address.");

        connection.Domain = domain;

        if (string.IsNullOrWhiteSpace(connection.BaseUrl))
            connection.BaseUrl = "https://api.securityscorecard.io";

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidParameterException(nameof(connection.BaseUrl),
                "The SecurityScorecard base URL must be an absolute https URL.");

        if (connection.SyncIntervalHours is < 1 or > 168)
            throw new InvalidParameterException(nameof(connection.SyncIntervalHours),
                "The sync interval must be between 1 and 168 hours.");
    }

    private static SecurityScorecardConnection Copy(SecurityScorecardConnection source,
        SecurityScorecardConnection target)
    {
        target.Name = source.Name.Trim();
        target.Domain = source.Domain;
        target.BaseUrl = source.BaseUrl.TrimEnd('/');
        target.EntityId = source.EntityId;
        target.Enabled = source.Enabled;
        target.SyncIntervalHours = source.SyncIntervalHours;
        target.SyncVulnerabilities = source.SyncVulnerabilities;
        target.SyncIssues = source.SyncIssues;
        return target;
    }

    private async Task<IntegrationSyncLog> BeginLogAsync(SecurityScorecardConnection connection)
    {
        await using var db = DalService.GetContext();

        var log = new IntegrationSyncLog
        {
            Integration = IntegrationKind.SecurityScorecard,
            ConnectionId = connection.Id,
            ConnectionName = connection.Name,
            StartedAt = DateTime.UtcNow,
            Status = IntegrationSyncStatus.Running
        };

        db.IntegrationSyncLogs.Add(log);
        await db.SaveChangesAsync();

        return log;
    }

    private async Task CompleteLogAsync(IntegrationSyncLog log, SecurityScorecardConnection connection,
        PostureSyncResult result, string? error)
    {
        await using var db = DalService.GetContext();

        var stored = await db.IntegrationSyncLogs.FirstOrDefaultAsync(l => l.Id == log.Id);

        var status = error != null
            ? IntegrationSyncStatus.Failed
            : result.Errors > 0
                ? IntegrationSyncStatus.PartiallySucceeded
                : IntegrationSyncStatus.Succeeded;

        if (stored != null)
        {
            stored.FinishedAt = DateTime.UtcNow;
            stored.Status = status;
            stored.CreatedCount = result.HostsCreated + result.FindingsCreated;
            stored.UpdatedCount = result.HostsUpdated + result.FindingsUpdated;
            stored.FailedCount = result.Errors;
            stored.Summary = Truncate(
                $"{result.PostureRowsWritten} posture row(s), {result.FindingsCreated} finding(s) created, "
                + $"{result.FindingsUpdated} updated"
                + (result.CyberRiskIndex == null ? "" : $", index {result.CyberRiskIndex}") + ".", 2000);
            stored.ErrorMessage = Truncate(error, 2000);
        }

        var storedConnection = await db.SecurityScorecardConnections
            .FirstOrDefaultAsync(c => c.Id == connection.Id);

        if (storedConnection != null)
        {
            storedConnection.LastSyncAt = DateTime.UtcNow;
            storedConnection.LastSyncStatus = status;
            storedConnection.LastSyncError = Truncate(error, 2000);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<SecurityScorecardConnection> LoadAsync(AuditableContext db, int id) =>
        await db.SecurityScorecardConnections.FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new DataNotFoundException("securityscorecard_connections", id.ToString(),
            new Exception($"SecurityScorecard connection {id} was not found."));

    private static SecurityScorecardConnectionView ToView(SecurityScorecardConnection connection) => new()
    {
        Id = connection.Id,
        Name = connection.Name,
        Domain = connection.Domain,
        BaseUrl = connection.BaseUrl,
        HasApiToken = !string.IsNullOrEmpty(connection.EncryptedApiToken),
        EntityId = connection.EntityId,
        Enabled = connection.Enabled,
        SyncIntervalHours = connection.SyncIntervalHours,
        SyncVulnerabilities = connection.SyncVulnerabilities,
        SyncIssues = connection.SyncIssues,
        LastSyncAt = connection.LastSyncAt,
        LastSyncStatus = connection.LastSyncStatus,
        LastSyncError = connection.LastSyncError
    };

    private static string? Truncate(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..(max - 1)] + "…";
}
