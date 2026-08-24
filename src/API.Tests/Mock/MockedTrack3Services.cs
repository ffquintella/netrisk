using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using Model.Findings;
using NSubstitute;
using ServerServices.Importers;
using ServerServices.Importers.Dedup;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// Importer discovery (Track 3 milestone 3.1.4). Two importers, one of them a plugin, so a test can
/// assert that the API does not distinguish between them.
/// </summary>
public static class MockedImporterRegistry
{
    public static IImporterRegistry Create()
    {
        var registry = Substitute.For<IImporterRegistry>();

        registry.GetImportersAsync().Returns(Task.FromResult(new List<ImporterDescriptor>
        {
            new()
            {
                Name = "nessus", DisplayName = "Tenable Nessus", Version = "2.0",
                ContractVersion = ImporterContract.Version,
                SupportedFileExtensions = [".nessus", ".xml"], IsPlugin = false,
                DedupStrategyChain = "HashBased,LegacyHashCode"
            },
            new()
            {
                Name = "acme-scanner", DisplayName = "Acme Scanner", Version = "1.0",
                ContractVersion = ImporterContract.Version,
                SupportedFileExtensions = [".json"], IsPlugin = true,
                DedupStrategyChain = "HashBased"
            }
        }));

        registry.ResolveAsync(Arg.Any<string>()).Returns(call =>
        {
            var name = call.ArgAt<string>(0);
            if (name is "nessus" or "acme-scanner")
                return Task.FromResult<IVulnerabilityReportImporter>(new NessusReportImporter());

            throw new DataNotFoundException("importers", name,
                new Exception($"Unknown importer '{name}'. Available importers: nessus, acme-scanner."));
        });

        registry.ResolveOrDetectAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                if (name is "nessus" or "acme-scanner" or "auto")
                    return Task.FromResult<IVulnerabilityReportImporter>(new NessusReportImporter());

                throw new DataNotFoundException("importers", name,
                    new Exception($"Unknown importer '{name}'. Available importers: nessus, acme-scanner."));
            });

        return registry;
    }
}

/// <summary>
/// The import log (Track 3 milestones 3.1.4 and 3.3.2). Import 1 succeeded; anything else is
/// unknown.
/// </summary>
public static class MockedFindingIngestionService
{
    public static IFindingIngestionService Create()
    {
        var service = Substitute.For<IFindingIngestionService>();

        service.GetImportAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("scan_imports", id.ToString(),
                    new Exception("Import not found"));

            return Task.FromResult(Succeeded());
        });

        service.GetRecentImportsAsync(Arg.Any<int>())
            .Returns(Task.FromResult(new List<ScanImport> { Succeeded() }));

        service.BeginImportAsync(Arg.Any<ImportIngestionRequest>()).Returns(call =>
        {
            var request = call.ArgAt<ImportIngestionRequest>(0);
            request.ExistingImportId = 1;

            // A repeated idempotency key resolves to the original import rather than starting work.
            return Task.FromResult(new ImportReservation(Succeeded(),
                IsReplay: request.IdempotencyKey == "already-used"));
        });

        service.IngestAsync(Arg.Any<ImportResult>(), Arg.Any<ImportIngestionRequest>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult(Succeeded()));

        service.FindByIdempotencyKeyAsync(Arg.Any<string>()).Returns(call =>
            Task.FromResult<ScanImport?>(call.ArgAt<string>(0) == "already-used" ? Succeeded() : null));

        return service;
    }

    private static ScanImport Succeeded() => new()
    {
        Id = 1,
        Importer = "nessus",
        FileName = "scan.nessus",
        Status = (int)ScanImportStatus.Succeeded,
        StartedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        FinishedAt = new DateTime(2026, 8, 1, 0, 1, 0, DateTimeKind.Utc),
        NewCount = 12,
        UpdatedCount = 4,
        DuplicateCount = 1,
        NewBySeverity = "{\"critical\":2,\"high\":10}"
    };
}

/// <summary>Deduplication configuration and preview (Track 3 milestone 3.3.3).</summary>
public static class MockedDeduplicationService
{
    public static IDeduplicationService Create()
    {
        var service = Substitute.For<IDeduplicationService>();

        service.GetConfigurationAsync(Arg.Any<string>()).Returns(call =>
            Task.FromResult(Configuration(call.ArgAt<string>(0))));

        service.GetConfigurationsAsync()
            .Returns(Task.FromResult(new List<ScannerDedupConfiguration> { Configuration("nessus") }));

        service.SaveConfigurationAsync(Arg.Any<ScannerDedupConfiguration>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var configuration = call.ArgAt<ScannerDedupConfiguration>(0);

                if (configuration.StrategyChain.Contains("Telepathy"))
                    throw new InvalidParameterException(nameof(configuration.StrategyChain),
                        "Unknown deduplication strategy: Telepathy.");

                return Task.FromResult(configuration);
            });

        service.GetConfigurationHistoryAsync(Arg.Any<string>())
            .Returns(Task.FromResult(new List<ScannerDedupConfigurationHistory>
            {
                new()
                {
                    Id = 1, Importer = "nessus", OldStrategyChain = "HashBased",
                    NewStrategyChain = "UniqueIdFromTool,HashBased", NewAutoCloseMissing = false,
                    ChangedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            }));

        service.KnownStrategyNamesAsync().Returns(Task.FromResult(new List<string>
        {
            "UniqueIdFromTool", "HashBased", "LegacyHashCode"
        }));

        service.PreviewAsync(Arg.Any<DedupContext>(), Arg.Any<DedupContext>(), Arg.Any<string>())
            .Returns(call =>
            {
                var left = call.ArgAt<DedupContext>(0);
                var right = call.ArgAt<DedupContext>(1);

                // Same title means same key here, which is enough to exercise both verdicts.
                var same = left.Finding.Title == right.Finding.Title;

                var leftKeys = new List<DedupCandidate> { new("HashBased", "aaaa", false) };
                var rightKeys = new List<DedupCandidate> { new("HashBased", same ? "aaaa" : "bbbb", false) };

                return Task.FromResult(new DedupPreview(
                    Configuration(call.ArgAt<string>(2)),
                    new DedupKeyResult(leftKeys),
                    new DedupKeyResult(rightKeys),
                    same ? ["aaaa"] : []));
            });

        return service;
    }

    private static ScannerDedupConfiguration Configuration(string importer) => new()
    {
        Id = 1,
        Importer = importer,
        StrategyChain = "UniqueIdFromTool,HashBased",
        HashFields = "tool,ruleId,asset,location,cve",
        AutoCloseMissing = false,
        CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

/// <summary>SLA policy and compliance (Track 3 milestone 3.4).</summary>
public static class MockedSlaService
{
    public static ISlaService Create()
    {
        var service = Substitute.For<ISlaService>();

        service.GetConfigurationsAsync(Arg.Any<bool>()).Returns(call =>
            Task.FromResult(call.ArgAt<bool>(0)
                ? new List<SlaConfiguration> { Sla(4, 2, 15), Sla(4, 3, 20, superseded: true), Sla(3, 5, 30) }
                : new List<SlaConfiguration> { Sla(4, 2, 15), Sla(3, 5, 30) }));

        service.SetConfigurationAsync(Arg.Any<SlaConfiguration>(), Arg.Any<int?>()).Returns(call =>
        {
            var configuration = call.ArgAt<SlaConfiguration>(0);

            if (configuration.MaxTriageDays > configuration.MaxRemediationDays)
                throw new InvalidParameterException(nameof(configuration.MaxTriageDays),
                    "The triage allowance cannot exceed the remediation allowance.");

            configuration.Id = 99;
            return Task.FromResult(configuration);
        });

        service.RecomputeDueDateAsync(Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("vulnerabilities", id.ToString(),
                    new Exception("Finding not found"));

            return Task.FromResult<DateTime?>(new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc));
        });

        service.GetComplianceBySeverityAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<SlaComplianceBucket>
            {
                new() { Severity = NormalizedSeverity.Critical, Total = 4, WithinSla = 3, Breached = 1 },
                new() { Severity = NormalizedSeverity.High, Total = 0, WithinSla = 0, Breached = 0 }
            }));

        return service;
    }

    private static SlaConfiguration Sla(int severity, int triage, int remediation, bool superseded = false) => new()
    {
        Id = severity, Severity = severity, MaxTriageDays = triage, MaxRemediationDays = remediation,
        EffectiveFrom = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EffectiveTo = superseded ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
        CreatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

/// <summary>CI API tokens (Track 3 milestone 3.5.1).</summary>
public static class MockedApiTokensService
{
    public const string IssuedSecret = "nrk_deadbeefdeadbeef_verySecretValue";

    public static IApiTokensService Create()
    {
        var service = Substitute.For<IApiTokensService>();

        service.IssueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<DateTime?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                var scopes = call.ArgAt<string>(1);

                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidParameterException("name", "An API token requires a name.");

                if (string.IsNullOrWhiteSpace(scopes))
                    throw new InvalidParameterException("scopes",
                        "An API token must grant at least one scope.");

                return Task.FromResult(new IssuedApiToken
                {
                    Id = 1, Name = name, KeyId = "deadbeefdeadbeef", Secret = IssuedSecret,
                    Scopes = ApiTokenScopes.Parse(scopes)
                });
            });

        service.GetTokensAsync(Arg.Any<bool>()).Returns(call =>
            Task.FromResult(call.ArgAt<bool>(0)
                ? new List<ApiToken> { Token(1, revoked: false), Token(2, revoked: true) }
                : new List<ApiToken> { Token(1, revoked: false) }));

        service.GetTokenAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("api_tokens", id.ToString(),
                    new Exception("API token not found"));

            return Task.FromResult(Token(1, revoked: false));
        });

        service.RevokeAsync(Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("api_tokens", id.ToString(),
                    new Exception("API token not found"));

            return Task.FromResult(Token(1, revoked: true));
        });

        return service;
    }

    private static ApiToken Token(int id, bool revoked) => new()
    {
        Id = id,
        Name = $"token-{id}",
        KeyId = $"key{id}",
        // A hash, never a secret — a test asserting this never reaches the wire is the point.
        SecretHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        Scopes = ApiTokenScopes.VulnerabilitiesImport,
        UserId = 1,
        CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        RevokedAt = revoked ? new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc) : null
    };
}
