using System;
using System.Linq;
using BackgroundJobs.Jobs.Cleanup;
using BackgroundJobs.Tests.DI;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Cleanup;

/// <summary>
/// Regression for the daily file cleanup deleting every file whose risk and mitigation were null — which
/// took the attachments of incidents, response plans, risk acceptances and assessment answers, and report
/// PDFs, with it. A file is an orphan only when it has no parent of any kind.
/// </summary>
[TestSubject(typeof(FileCleanup))]
public class FileCleanupTest
{
    private static readonly DateTime Old = DateTime.Now - FileCleanup.GracePeriod - TimeSpan.FromDays(1);

    private readonly InMemoryFileDal _dal = new($"file-cleanup-{Guid.NewGuid()}");

    private int Seed(Action<NrFile>? parent = null, DateTime? timestamp = null, bool report = false)
    {
        using var db = _dal.GetContext();
        var file = new NrFile
        {
            Name = "f", UniqueName = Guid.NewGuid().ToString("N"), Content = [1], Size = 1,
            Timestamp = timestamp ?? Old
        };
        parent?.Invoke(file);
        db.NrFiles.Add(file);
        db.SaveChanges();
        if (report)
        {
            db.Reports.Add(new Report { Name = "r", FileId = file.Id, CreationDate = Old });
            db.SaveChanges();
        }
        return file.Id;
    }

    private bool Exists(int id)
    {
        using var db = _dal.GetContext();
        return db.NrFiles.Any(f => f.Id == id);
    }

    private void Run() => new FileCleanup(TestDoubles.Logger(), _dal).Run();

    public static TheoryData<string, Action<NrFile>> Parents => new()
    {
        { "risk", f => f.RiskId = 1 },
        { "mitigation", f => f.MitigationId = 1 },
        { "risk acceptance", f => f.RiskAcceptanceId = 1 },
        { "incident", f => f.IncidentId = 1 },
        { "incident response plan", f => f.IncidentResponsePlanId = 1 },
        { "incident response plan execution", f => f.IncidentResponsePlanExecutionId = 1 },
        { "incident response plan task", f => f.IncidentResponsePlanTaskId = 1 },
        { "incident response plan task execution", f => f.IncidentResponsePlanTaskExecutionId = 1 },
        { "assessment run answer", f => f.AssessmentRunAnswerId = 1 },
    };

    [Theory]
    [MemberData(nameof(Parents))]
    public void TestRunKeepsOldFileWithAnyKindOfParent(string kind, Action<NrFile> parent)
    {
        var id = Seed(parent);

        Run();

        Assert.True(Exists(id), $"a file attached to a {kind} was deleted");
    }

    [Fact]
    public void TestRunKeepsReportPdfReferencedByAReport()
    {
        var id = Seed(f => f.ViewType = 1, report: true);

        Run();

        Assert.True(Exists(id));
    }

    [Fact]
    public void TestRunDeletesOldFileWithNoParent()
    {
        var orphan = Seed();
        var kept = Seed(f => f.IncidentId = 7);

        Run();

        Assert.False(Exists(orphan));
        Assert.True(Exists(kept));
    }

    [Fact]
    public void TestRunKeepsParentlessFileInsideGracePeriod()
    {
        var upload = Seed(timestamp: DateTime.Now - TimeSpan.FromHours(1));

        Run();

        Assert.True(Exists(upload));
    }

    [Fact]
    public void TestRunWithNothingToCleanDoesNothing()
    {
        Run();
    }

    private sealed class InMemoryFileDal(string name) : ServerServices.Services.DalService(Config(), new Microsoft.AspNetCore.Http.HttpContextAccessor())
    {
        private readonly DbContextOptions<NRDbContext> _options =
            new DbContextOptionsBuilder<NRDbContext>().UseInMemoryDatabase(name).Options;

        public override AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
            new(_options) { EntityScope = EntityScope.Unrestricted };

        private static Microsoft.Extensions.Configuration.IConfiguration Config() =>
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = "server=localhost;database=netrisk_tests"
                }).Build();
    }
}
