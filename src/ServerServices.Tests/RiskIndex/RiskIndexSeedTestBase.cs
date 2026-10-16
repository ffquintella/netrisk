using System;
using System.Text;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Model;
using ServerServices.Tests.ServiceTests;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// A small organization shaped like Unit X (S39 §11), seeded into the in-memory database for the collector and the day
/// calculator: the entity map with EAV links and risk context, hosts, findings, a SecurityScorecard connection,
/// incidents, an assessment and a scenario with its register history. <see cref="Now"/> is the snapshot instant.
/// </summary>
public abstract class RiskIndexSeedTestBase : InMemoryServiceTestBase
{
    protected static readonly DateTime Now = new(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);

    protected const int Org = 1, UnitX = 10, Lab = 12, Enrolment = 20, Portal = 30, Invalid = 40;
    protected const int Srv = 101, Db = 102, Tst = 103, WsAdm = 104, Retired = 105, Pseudo = 106, Accepted = 107,
        Bkp = 108, LabHost = 109, ProcessHost = 110;
    protected const int Reviewer = 99, Owner = 98;

    private int _propertyId = 5000;
    private int _findingId = 9000;

    protected RiskIndexSeedTestBase()
    {
        SeedUnscoped(ctx =>
        {
            AddEntity(ctx, Org, "organization", null);
            AddEntity(ctx, UnitX, "organizationUnit", Org, ("criticality", "3"));
            AddEntity(ctx, Lab, "subOrganizationUnit", UnitX, ("criticality", "2"));
            AddEntity(ctx, Enrolment, "businessProcess", null, ("organizationUnit", UnitX.ToString()),
                ("applications", Portal.ToString()), ("criticality", "5"));
            AddEntity(ctx, Portal, "application", null, ("criticality", "5"), ("internetFacing", "True"));
            AddEntity(ctx, Invalid, "organizationUnit", Org, ("criticality", "9"));

            ctx.Hosts.AddRange(
                NewHost(Srv, UnitX, 5, riskScore: 90),
                NewHost(Db, UnitX, 4),
                NewHost(Tst, UnitX, 1, riskScore: 10),
                NewHost(WsAdm, UnitX, 3),
                NewHost(Retired, UnitX, 5, riskScore: 99, status: 27),
                NewHost(Pseudo, UnitX, 5, provider: "SecurityScorecard"),
                NewHost(Accepted, UnitX, 3),
                NewHost(Bkp, UnitX, 4),
                NewHost(LabHost, Lab, 2, riskScore: 25),
                NewHost(ProcessHost, Enrolment, 3, riskScore: 60));

            // SRV-01: a Vision One CVE (out of EXP while the device score stands) six days past a 15-day SLA.
            ctx.Vulnerabilities.Add(Finding(Srv, "trendmicro-visionone", severity: "4", cvss3: 9.8f, cve: "CVE-2026-0001",
                sla: Now.Date.AddDays(-6), firstSeen: Now.Date.AddDays(-21)));

            // DB-02: VPR 6.7, and two CVSS 5.3 CVEs with an exploit available; the VPR CVE seen again by another tool.
            ctx.Vulnerabilities.Add(Finding(Db, "nessus", vpr: 6.7f, cvss3: 7.5f, cve: "CVE-2026-0002"));
            ctx.Vulnerabilities.Add(Finding(Db, "nessus", cvss3: 5.3f, exploit: true, cve: "CVE-2026-0003"));
            ctx.Vulnerabilities.Add(Finding(Db, "nessus", cvss3: 5.3f, exploit: true, cve: "CVE-2026-0004"));
            ctx.Vulnerabilities.Add(Finding(Db, "openvas", cvss3: 6.0f, cve: "CVE-2026-0002"));

            // ACCEPTED: a risk-accepted finding stays (I10); false positive, SSC issue and assessment finding never score.
            ctx.Vulnerabilities.Add(Finding(Accepted, "nessus", cvss3: 7.0f, cve: "CVE-2026-0010", lifecycle: FindingStatus.RiskAccepted));
            ctx.Vulnerabilities.Add(Finding(Accepted, "nessus", cvss3: 9.8f, cve: "CVE-2026-0011", lifecycle: FindingStatus.FalsePositive));
            ctx.Vulnerabilities.Add(Finding(Accepted, "securityscorecard", cvss3: 9.8f, cve: "CVE-2026-0012"));
            ctx.Vulnerabilities.Add(Finding(Accepted, "assessment", cvss3: 9.8f, cve: "CVE-2026-0013"));

            // BKP-04: its last scan, 45 days ago, found nothing open.
            ctx.Vulnerabilities.Add(Finding(Bkp, "nessus", cvss3: 5.0f, cve: "CVE-2026-0020", lifecycle: FindingStatus.Mitigated,
                lastSeen: Now.AddDays(-45)));

            // APP-03: SAST findings without host — one High, three Medium; imported two days ago for the application.
            ctx.Vulnerabilities.Add(Finding(null, "semgrep", severity: "High", entity: Portal, rule: "sql-injection"));
            for (var i = 0; i < 3; i++)
                ctx.Vulnerabilities.Add(Finding(null, "semgrep", severity: "Medium", entity: Portal, rule: $"xss-{i}"));
            ctx.ScanImports.Add(new ScanImport { Id = 76, Importer = "semgrep", EntityId = Portal, StartedAt = Now.AddDays(-2), FinishedAt = Now.AddDays(-2) });

            // A host-less finding imported by an unrestricted caller: no entity, grouped as Unassigned.
            ctx.Vulnerabilities.Add(Finding(null, "sarif", severity: "Low", rule: "weak-hash", import: 77));
            ctx.ScanImports.Add(new ScanImport { Id = 77, Importer = "sarif", StartedAt = Now.AddDays(-1), FinishedAt = Now.AddDays(-1) });

            ctx.SecurityScorecardConnections.Add(new SecurityScorecardConnection
            {
                Id = 1, Name = "edu", Domain = "example.invalid", EntityId = UnitX, Enabled = true, CreatedAt = Now.AddYears(-1)
            });
            ctx.SecurityScorecardFactors.Add(new SecurityScorecardFactor { Id = 1, ConnectionId = 1, FactorName = "overall", Score = 90, IsOverall = true, CapturedAt = Now.AddDays(-30) });
            ctx.SecurityScorecardFactors.Add(new SecurityScorecardFactor { Id = 2, ConnectionId = 1, FactorName = "overall", Score = 72, IsOverall = true, CapturedAt = Now.AddDays(-1) });

            ctx.Incidents.Add(new Incident
            {
                Id = 1, Name = "2026-1", Description = "phishing", Category = "phishing", EntityId = UnitX,
                Status = (int)IntStatus.Closed, CreationDate = Now.AddDays(-40), LastUpdate = Now.AddDays(-20), CreatedById = 1
            });
            ctx.Incidents.Add(new Incident
            {
                Id = 2, Name = "2026-2", Description = "intrusion", Category = "unauthorized_access", ImpactedEntityId = Portal,
                EntityId = UnitX, Status = (int)IntStatus.New, CreationDate = Now.AddDays(-5), LastUpdate = Now.AddDays(-5), CreatedById = 1
            });

            ctx.Assessments.Add(new Assessment { Id = 1, Name = "ISO", Created = Now.AddYears(-1) });
            ctx.AssessmentQuestions.Add(new AssessmentQuestion { Id = 1, AssessmentId = 1, Question = "Q", Order = 1 });
            ctx.AssessmentAnswers.Add(Option(1, 0));
            ctx.AssessmentAnswers.Add(Option(2, 3.5f));
            ctx.AssessmentAnswers.Add(Option(3, 10));
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = 1, AssessmentId = 1, EntityId = UnitX, Status = 1, RunDate = Now.AddDays(-10) });
            ctx.AssessmentRunsAnswers.Add(new AssessmentRunsAnswer { Id = 1, RunId = 1, QuestionId = 1, AnswerId = 2 });

            ctx.RiskAppetites.Add(new RiskAppetite { Id = 1, EntityId = null, MaxAcceptableResidual = 5.0, CreatedAt = Now.AddYears(-1) });

            var process = ctx.Entities.Find(Enrolment)!;
            var risk = NewRisk(1, UnitX, "New");
            risk.Entities.Add(process);
            ctx.Risks.Add(risk);
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 8.8f, ResidualRisk = 8.4f, ClassicImpact = 5, ScoringMethod = 1 });
            ctx.Risks.Add(NewRisk(2, UnitX, "Closed"));
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 9f, ScoringMethod = 1 });
        });

        // The qualified review and the acceptance are separate saves: one save would correlate them in audit_logs.
        SeedUnscoped(ctx => ctx.MgmtReviews.Add(Review(1, new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), Reviewer)));
        SeedUnscoped(ctx => ctx.RiskAcceptances.Add(new RiskAcceptance
        {
            Id = 1, Name = "R1", RiskId = 1, StartDate = new DateTime(2026, 3, 20), CreatedAt = new DateTime(2026, 3, 20),
            ExpiresAt = new DateTime(2026, 9, 20), Status = RiskAcceptanceStatus.Expired, AuthorizingManagerId = 50, CreatedById = 50
        }));
    }

    protected static void AddEntityProperties(AuditableContext ctx, int entity, ref int id, params (string Key, string Value)[] properties)
    {
        foreach (var (key, value) in properties)
            ctx.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = ++id, Entity = entity, Type = key, Name = key, Value = value, OldValue = ""
            });
    }

    private void AddEntity(AuditableContext ctx, int id, string definition, int? parent, params (string Key, string Value)[] properties)
    {
        ctx.Entities.Add(new Entity
        {
            Id = id, DefinitionName = definition, DefinitionVersion = "2.7", Status = "active", Parent = parent,
            Created = Now.AddYears(-2), Updated = Now.AddYears(-2)
        });
        AddEntityProperties(ctx, id, ref _propertyId, properties);
    }

    private static Host NewHost(int id, int entity, int criticality, int? riskScore = null, short status = 1,
        string? provider = null) => new()
    {
        Id = id, HostName = $"host-{id}", Ip = $"10.0.0.{id % 250}", EntityId = entity, Source = "test", Status = status,
        Criticality = criticality, RiskScore = riskScore, RiskScoreUpdatedAt = riskScore is null ? null : Now.AddDays(-1),
        ExternalProvider = provider, RegistrationDate = Now.AddDays(-100)
    };

    protected Vulnerability Finding(int? host, string source, string? severity = null, float? cvss3 = null, float? vpr = null,
        bool exploit = false, string? cve = null, FindingStatus lifecycle = FindingStatus.Active, DateTime? sla = null,
        DateTime? firstSeen = null, DateTime? lastSeen = null, int? entity = null, string? rule = null, int? import = null) => new()
    {
        Id = ++_findingId, HostId = host, EntityId = entity, ImportSource = source, Severity = severity, Cvss3BaseScore = cvss3,
        VprScore = vpr, ExploitAvaliable = exploit, Cves = cve, LifecycleStatus = lifecycle, SlaDueDate = sla,
        FirstDetection = firstSeen ?? Now.AddDays(-30), LastDetection = lastSeen ?? Now.AddDays(-1),
        Title = $"finding {_findingId}", RuleId = rule, LastImportId = import, Status = (ushort)IntStatus.New, DetectionCount = 1
    };

    private static AssessmentAnswer Option(int id, float score) => new()
    {
        Id = id, AssessmentId = 1, QuestionId = 1, Answer = $"option {id}", RiskSubject = Encoding.UTF8.GetBytes("s"),
        RiskScore = score, Order = id
    };

    protected static Risk NewRisk(int id, int? entity, string status) => new()
    {
        Id = id, EntityId = entity, Status = status, Subject = $"Risk {id}", ReferenceId = $"R-{id}", Assessment = "",
        Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = new DateTime(2026, 1, 10),
        LastUpdate = new DateTime(2026, 1, 10), Owner = Owner, SubmittedBy = Owner
    };

    protected static MgmtReview Review(int riskId, DateTime at, int reviewer) => new()
    {
        RiskId = riskId, SubmissionDate = at, Reviewer = reviewer, Comments = "", Review = 1, NextStep = 1,
        NextReview = DateOnly.FromDateTime(at.AddDays(90))
    };
}
