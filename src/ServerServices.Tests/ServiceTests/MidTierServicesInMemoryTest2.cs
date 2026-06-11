using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

internal static class MidTierFixtures
{
    public static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "Open", Subject = "S", ReferenceId = "R", Assessment = "",
        Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = ""
    };

    public static MgmtReview NewReview(int id, int riskId) => new()
    {
        Id = id, RiskId = riskId, Review = 1, Reviewer = 1, NextStep = 1, Comments = "",
        SubmissionDate = new DateTime(2026, 1, 1)
    };
}

public class MgmtReviewsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IMgmtReviewsService _svc;
    public MgmtReviewsServiceInMemoryTest() => _svc = GetService<IMgmtReviewsService>();

    [Fact]
    public void TestGetRiskReviews()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(MidTierFixtures.NewRisk(1));
            ctx.MgmtReviews.Add(MidTierFixtures.NewReview(1, 1));
        });

        Assert.Single(_svc.GetRiskReviews(1));
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskReviews(99));
    }

    [Fact]
    public void TestReviewTypesAndNextSteps()
    {
        Seed(ctx =>
        {
            ctx.Reviews.Add(new Review { Value = 1, Name = "Annual" });
            ctx.NextSteps.Add(new NextStep { Value = 1, Name = "Mitigate" });
        });

        Assert.Single(_svc.GetReviewTypes());
        Assert.Single(_svc.GetNextSteps());
    }

    private static void SeedReviewLookups(DAL.Context.AuditableContext ctx)
    {
        // Review/NextStep are required navigations on MgmtReview; the service Includes them,
        // which EF treats as an inner join, so the principals must exist.
        ctx.Reviews.Add(new Review { Value = 1, Name = "Annual" });
        ctx.NextSteps.Add(new NextStep { Value = 1, Name = "Mitigate" });
    }

    [Fact]
    public void TestCreateAndGetOne()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(MidTierFixtures.NewRisk(1));
            SeedReviewLookups(ctx);
        });

        var created = _svc.Create(MidTierFixtures.NewReview(0, 1));
        Assert.NotNull(created);

        Assert.Equal(created.Id, _svc.GetOne(created.Id).Id);
        Assert.Throws<DataNotFoundException>(() => _svc.GetOne(999));
    }

    [Fact]
    public void TestGetRiskLastReview()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(MidTierFixtures.NewRisk(1));
            SeedReviewLookups(ctx);
            var r1 = MidTierFixtures.NewReview(1, 1); r1.SubmissionDate = new DateTime(2026, 1, 1);
            var r2 = MidTierFixtures.NewReview(2, 1); r2.SubmissionDate = new DateTime(2026, 5, 1);
            ctx.MgmtReviews.Add(r1);
            ctx.MgmtReviews.Add(r2);
        });

        var last = _svc.GetRiskLastReview(1);

        Assert.NotNull(last);
    }

    [Fact]
    public void TestUpdate()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(MidTierFixtures.NewRisk(1));
            ctx.MgmtReviews.Add(MidTierFixtures.NewReview(1, 1));
        });

        var updated = _svc.Update(new MgmtReviewDto { Id = 1, RiskId = 1, Review = 2, Reviewer = 1, NextStep = 1, Comments = "x" });

        Assert.NotNull(updated);
        Assert.Throws<DataNotFoundException>(() =>
            _svc.Update(new MgmtReviewDto { Id = 99, RiskId = 1, Comments = "x" }));
    }

    [Fact]
    public void TestGetRiskReviewLevelNotFoundPaths()
    {
        // RiskLevel is keyless and cannot be seeded via the in-memory provider, so we cover
        // the guard clauses: missing risk and missing scoring both throw.
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskReviewLevel(99));

        Seed(ctx => ctx.Risks.Add(MidTierFixtures.NewRisk(1)));
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskReviewLevel(1)); // scoring missing
    }
}

public class FixRequestsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IFixRequestsService _svc;
    public FixRequestsServiceInMemoryTest() => _svc = GetService<IFixRequestsService>();

    private static FixRequest NewFr(int id, string ident = "FR", int vulnId = 10) => new()
    {
        Id = id, Identifier = ident, VulnerabilityId = vulnId, Status = 1,
        CreationDate = new DateTime(2026, 1, 1)
    };

    private static Vulnerability NewVuln(int id) => new()
    {
        Id = id, Title = "V", Status = 1,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 1), DetectionCount = 1
    };

    [Fact]
    public async Task TestCreateGetByIdAndIdentifier()
    {
        // Vulnerability is a required navigation that the service Includes (inner join).
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(10)));

        var created = await _svc.CreateFixRequestAsync(NewFr(0, "FR-1"));

        Assert.Equal("FR-1", (await _svc.GetByIdAsync(created.Id)).Identifier);
        Assert.Equal(created.Id, (await _svc.GetFixRequestAsync("FR-1")).Id);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetByIdAsync(999));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetFixRequestAsync("none"));
    }

    [Fact]
    public async Task TestGetAllAndSave()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(10)));

        var created = await _svc.CreateFixRequestAsync(NewFr(0, "FR-2"));
        Assert.Single(await _svc.GetAllFixRequestAsync());

        created.Status = 2;
        var saved = await _svc.SaveFixRequestAsync(created);
        Assert.Equal(2, saved.Status);
    }

    [Fact]
    public async Task TestGetVulnerabilitiesFixRequest()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(10));
            ctx.Vulnerabilities.Add(NewVuln(20));
        });

        await _svc.CreateFixRequestAsync(NewFr(0, "A", vulnId: 10));
        await _svc.CreateFixRequestAsync(NewFr(0, "B", vulnId: 20));

        var list = await _svc.GetVulnerabilitiesFixRequestAsync(new List<int> { 10 });

        Assert.Single(list);
    }
}

public class LinksServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly ILinksService _svc;
    public LinksServiceInMemoryTest() => _svc = GetService<ILinksService>();

    [Fact]
    public void TestCreateAndCheckLink()
    {
        var url = _svc.CreateLink("reset", DateTime.Now.AddDays(1), new byte[] { 1, 2, 3 });

        Assert.Contains("key=", url);
        var key = url.Split("key=")[1];

        Assert.True(_svc.LinkExists("reset", key));
        Assert.False(_svc.LinkExists("reset", "wrongkey"));
    }

    [Fact]
    public void TestDeleteLink()
    {
        var url = _svc.CreateLink("reset", DateTime.Now.AddDays(1), null);
        var key = url.Split("key=")[1];

        _svc.DeleteLink("reset", key);

        Assert.False(_svc.LinkExists("reset", key));
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteLink("reset", key));
    }
}

public class TechnologiesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly ITechnologiesService _svc;
    public TechnologiesServiceInMemoryTest() => _svc = GetService<ITechnologiesService>();

    [Fact]
    public async Task TestAddGetRemove()
    {
        await _svc.AddTechnologyAsync("dotnet");

        Assert.Single(_svc.GetAll());
        Assert.Single(await _svc.GetAllAsync());

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => _svc.AddTechnologyAsync("dotnet"));

        await _svc.RemoveTechnologyAsync("dotnet");
        Assert.Empty(_svc.GetAll());
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.RemoveTechnologyAsync("dotnet"));
    }
}

public class JobsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IJobsService _svc;
    public JobsServiceInMemoryTest() => _svc = GetService<IJobsService>();

    [Fact]
    public async Task TestJobLifecycle()
    {
        var jobId = await _svc.RegisterJobAsync("Import");
        Assert.True(jobId > 0);

        await _svc.RegisterJobStartAsync(jobId, CancellationToken.None, userId: 3);
        await _svc.UpdateJobProgressAsync(jobId, 50);
        await _svc.RegisterJobEndAsync(jobId, "done");

        using var ctx = OpenContext();
        var job = ctx.Jobs.First(j => j.Id == jobId);
        Assert.Equal(100, job.Progress);
        Assert.Equal(3, job.OwnerId);
    }

    [Fact]
    public async Task TestJobFailAndCancel()
    {
        var failId = await _svc.RegisterJobAsync("Fail");
        await _svc.RegisterJobFailedAsync(failId, "boom");

        var cancelId = await _svc.RegisterJobAsync("Cancel");
        await _svc.CancelJobAsync(cancelId);

        using var ctx = OpenContext();
        Assert.NotNull(ctx.Jobs.First(j => j.Id == failId).Result);
        Assert.NotNull(ctx.Jobs.First(j => j.Id == cancelId).Result);
    }

    [Fact]
    public async Task TestMissingJobThrows()
    {
        await Assert.ThrowsAsync<Exception>(() => _svc.UpdateJobProgressAsync(999, 1));
        await Assert.ThrowsAsync<Exception>(() => _svc.RegisterJobEndAsync(999, "x"));
        await Assert.ThrowsAsync<Exception>(() => _svc.CancelJobAsync(999));
        await Assert.ThrowsAsync<Exception>(() => _svc.RegisterJobFailedAsync(999, "x"));
    }
}

public class RiskCalculationServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskCalculationService _svc;
    public RiskCalculationServiceInMemoryTest() => _svc = GetService<IRiskCalculationService>();

    [Fact]
    public async Task TestCalculateRiskScore()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(MidTierFixtures.NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, ClassicImpact = 2, ClassicLikelihood = 2 });
        });

        await _svc.CalculateRiskScoreAsync();

        using var ctx = OpenContext();
        Assert.NotNull(ctx.RiskScorings.First(rs => rs.Id == 1));
    }

    [Fact]
    public async Task TestCalculateContributingImpact()
    {
        Seed(ctx =>
        {
            var risk = MidTierFixtures.NewRisk(1);
            risk.Vulnerabilities.Add(new Vulnerability
            {
                Id = 1, Title = "V", Status = (int)Model.IntStatus.Active, Score = 8,
                FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 1), DetectionCount = 1
            });
            ctx.Risks.Add(risk);
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, ClassicImpact = 2, ClassicLikelihood = 2 });
        });

        await _svc.CalculateContributingImpactAsync();

        using var ctx = OpenContext();
        Assert.NotNull(ctx.RiskScorings.First(rs => rs.Id == 1).ContributingScore);
    }
}
