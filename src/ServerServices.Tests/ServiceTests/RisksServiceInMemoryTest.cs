using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Sieve.Models;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(RisksService))]
public class RisksServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRisksService _svc;

    public RisksServiceInMemoryTest()
    {
        _svc = GetService<IRisksService>();
    }

    private static Risk NewRisk(int id, string status = "New", string subject = "Subj", int owner = 1) => new()
    {
        Id = id,
        Status = status,
        Subject = subject,
        ReferenceId = $"REF-{id}",
        Assessment = "",
        Notes = "",
        RiskCatalogMapping = "",
        ThreatCatalogMapping = "",
        Owner = owner,
        Manager = owner,
        SubmittedBy = owner,
        Source = 1,
        Category = 1
    };

    private void SeedLookups()
    {
        Seed(ctx =>
        {
            ctx.Sources.Add(new Source { Value = 1, Name = "Source1" });
            ctx.Categories.Add(new Category { Value = 1, Name = "Cat1" });
        });
    }

    [Fact]
    public void TestGetRiskNotFoundThrows()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.GetRisk(999));
    }

    [Fact]
    public void TestGetRiskReturnsRisk()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1, subject: "Hello")));

        var risk = _svc.GetRisk(1);

        Assert.Equal(1, risk.Id);
        Assert.Equal("Hello", risk.Subject);
    }

    [Fact]
    public async Task TestGetAllAsyncFiltersByStatus()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "New"));
            ctx.Risks.Add(NewRisk(2, status: "Closed"));
            ctx.Risks.Add(NewRisk(3, status: "Open"));
        });

        var all = await _svc.GetAllAsync();          // excludes Closed by default
        var open = await _svc.GetAllAsync("Open");

        Assert.Equal(2, all.Count);
        Assert.Single(open);
        Assert.Equal(3, open[0].Id);
    }

    [Fact]
    public void TestGetUserRisksNullUserThrows()
    {
        Assert.Throws<InvalidParameterException>(() => _svc.GetUserRisks(null!, null));
    }

    [Fact]
    public void TestGetUserRisksAdminReturnsAll()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "Open"));
            ctx.Risks.Add(NewRisk(2, status: "Open"));
        });

        var risks = _svc.GetUserRisks(new User { Value = 1, Admin = true }, null);

        Assert.Equal(2, risks.Count);
    }

    [Fact]
    public void TestGetUserRisksNonAdminScopedToUser()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Roles.Add(new Role { Value = 1, Name = "Reader" });
            ctx.Users.Add(new User { Value = 5, Type = "local", Name = "U5", Email = new byte[] { 1 }, Password = new byte[] { 1 }, Enabled = true });
            ctx.Risks.Add(NewRisk(1, status: "Open", owner: 5));
            ctx.Risks.Add(NewRisk(2, status: "Open", owner: 99));
        });

        var risks = _svc.GetUserRisks(new User { Value = 5, Admin = false, RoleId = 1 }, status: null, notStatus: null);

        Assert.Single(risks);
        Assert.Equal(1, risks[0].Id);
    }

    [Fact]
    public void TestSubjectExists()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1, subject: "Unique")));

        Assert.True(_svc.SubjectExists("Unique"));
        Assert.False(_svc.SubjectExists("Missing"));
    }

    [Fact]
    public void TestCreateRiskPopulatesNavigationAndPersists()
    {
        SeedLookups();

        var created = _svc.CreateRisk(NewRisk(0, subject: "Created"));

        Assert.NotNull(created);
        Assert.Equal("Created", created!.Subject);
        Assert.NotNull(created.SourceNavigation);
        Assert.NotNull(created.CategoryNavigation);
        using var ctx = OpenContext();
        Assert.Single(ctx.Risks.ToList());
    }

    [Fact]
    public void TestCreateRiskMissingSourceThrows()
    {
        Seed(ctx => ctx.Categories.Add(new Category { Value = 1, Name = "Cat1" }));

        Assert.Throws<DataNotFoundException>(() => _svc.CreateRisk(NewRisk(0)));
    }

    [Fact]
    public async Task TestCreateRiskAsyncWithCatalogs()
    {
        SeedLookups();
        Seed(ctx => ctx.RiskCatalogs.Add(new RiskCatalog
            { Id = 7, Number = "N", Name = "C", Description = "D" }));

        var risk = NewRisk(0, subject: "WithCat");
        risk.RiskCatalogs = new List<RiskCatalog> { new() { Id = 7 } };

        var created = await _svc.CreateRiskAsync(risk);

        Assert.NotNull(created);
        Assert.Single(created!.RiskCatalogs);
    }

    [Fact]
    public void TestDeleteRisk()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        _svc.DeleteRisk(1);

        using var ctx = OpenContext();
        Assert.Empty(ctx.Risks.ToList());
    }

    [Fact]
    public void TestDeleteRiskNotFoundThrows()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteRisk(123));
    }

    [Fact]
    public void TestGetRiskCategoryAndCategories()
    {
        SeedLookups();

        Assert.Equal("Cat1", _svc.GetRiskCategory(1).Name);
        Assert.Single(_svc.GetRiskCategories());
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskCategory(999));
    }

    [Fact]
    public void TestGetRiskSourceAndSources()
    {
        SeedLookups();

        Assert.Equal("Source1", _svc.GetRiskSource(1).Name);
        Assert.Single(_svc.GetRiskSources());
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskSource(999));
    }

    [Fact]
    public async Task TestGetRiskProbabilitiesAndImpacts()
    {
        // Likelihood/Impact are keyless entities; the in-memory provider cannot track them,
        // so we exercise the query path against an empty set (still returns a non-null list).
        Assert.NotNull(_svc.GetRiskProbabilities());
        Assert.NotNull(_svc.GetRiskImpacts());
        Assert.NotNull(await _svc.GetRiskImpactsAsync());
    }

    [Fact]
    public void TestGetRiskScoreNotFoundThrows()
    {
        // CustomRiskModelValue is keyless; the in-memory provider cannot seed it, so we
        // exercise the not-found path (the lookup query still executes).
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskScore(9, 9));
    }

    [Fact]
    public void TestGetRiskCloseReasons()
    {
        Seed(ctx => ctx.CloseReasons.Add(new CloseReason { Value = 1, Name = "Done" }));

        Assert.Single(_svc.GetRiskCloseReasons());
    }

    [Fact]
    public void TestClosureLifecycle()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        Assert.False(_svc.ClosureExists(1));

        var closure = _svc.CreateRiskClosure(new Closure { RiskId = 1, Note = "n", UserId = 1 });
        Assert.NotNull(closure);
        Assert.True(_svc.ClosureExists(1));
        Assert.Equal(1, _svc.GetRiskClosureByRiskId(1).RiskId);

        // duplicate closure is rejected
        Assert.Throws<DataAlreadyExistsException>(() =>
            _svc.CreateRiskClosure(new Closure { RiskId = 1, Note = "x", UserId = 1 }));

        _svc.DeleteRiskClosure(1);
        Assert.False(_svc.ClosureExists(1));
    }

    [Fact]
    public void TestGetRiskClosureMissingThrows()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskClosureByRiskId(50));
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteRiskClosure(50));
    }

    [Fact]
    public void TestRiskCatalogGetters()
    {
        Seed(ctx =>
        {
            ctx.RiskCatalogs.Add(new RiskCatalog { Id = 1, Number = "1", Name = "A", Description = "d" });
            ctx.RiskCatalogs.Add(new RiskCatalog { Id = 2, Number = "2", Name = "B", Description = "d" });
        });

        Assert.Equal("A", _svc.GetRiskCatalog(1).Name);
        Assert.Equal(2, _svc.GetRiskCatalogs().Count);
        Assert.Single(_svc.GetRiskCatalogs(new List<int> { 1 }));
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskCatalog(99));
    }

    [Fact]
    public void TestRiskScoringLifecycle()
    {
        Seed(ctx => { });

        var created = _svc.CreateRiskScoring(new RiskScoring { Id = 1, CalculatedRisk = 5f });
        Assert.NotNull(created);
        Assert.Equal(5f, _svc.GetRiskScoring(1).CalculatedRisk);

        // duplicate rejected
        Assert.Throws<DataAlreadyExistsException>(() =>
            _svc.CreateRiskScoring(new RiskScoring { Id = 1, CalculatedRisk = 1f }));

        _svc.SaveRiskScoring(new RiskScoring { Id = 1, CalculatedRisk = 9f });
        Assert.Equal(9f, _svc.GetRiskScoring(1).CalculatedRisk);

        _svc.DeleteRiskScoring(1);
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskScoring(1));
    }

    [Fact]
    public void TestGetRisksScoring()
    {
        Seed(ctx =>
        {
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 1f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 2f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 3, CalculatedRisk = 3f });
        });

        var scorings = _svc.GetRisksScoring(new List<int> { 1, 3 });

        Assert.Equal(2, scorings.Count);
    }

    [Fact]
    public void TestSaveRisk()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1, subject: "Before")));

        var update = NewRisk(1, subject: "After");
        update.RiskCatalogs = new List<RiskCatalog>();
        _svc.SaveRisk(update);

        Assert.Equal("After", _svc.GetRisk(1).Subject);
    }

    [Fact]
    public void TestAssociateAndCleanRiskEntity()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.Entities.Add(new Entity { Id = 1, DefinitionName = "asset", DefinitionVersion = "1", Status = "active" });
        });

        _svc.AssociateRiskWithEntity(1, 1);
        Assert.Equal(1, _svc.GetRiskEntityByRiskId(1).Id);

        _svc.DeleteEntityAssociation(1, 1);
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskEntityByRiskId(1));

        _svc.AssociateRiskWithEntity(1, 1);
        _svc.CleanRiskEntityAssociations(1);
        Assert.Throws<DataNotFoundException>(() => _svc.GetRiskEntityByRiskId(1));
    }

    [Fact]
    public void TestAssociateRiskEntityNotFoundThrows()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        Assert.Throws<DataNotFoundException>(() => _svc.AssociateRiskWithEntity(1, 99));
        Assert.Throws<DataNotFoundException>(() => _svc.AssociateRiskWithEntity(99, 1));
    }

    [Fact]
    public async Task TestGetVulnerabilities()
    {
        SeedLookups();
        Seed(ctx =>
        {
            var risk = NewRisk(1);
            risk.Vulnerabilities.Add(new Vulnerability { Id = 1, Status = (int)IntStatus.Active, Title = "V1" });
            risk.Vulnerabilities.Add(new Vulnerability { Id = 2, Status = (int)IntStatus.Closed, Title = "V2" });
            ctx.Risks.Add(risk);
        });

        var all = _svc.GetVulnerabilities(1);
        var openOnly = await _svc.GetVulnerabilitiesAsync(1);

        Assert.Equal(2, all.Count);
        Assert.Single(openOnly);  // closed filtered out
    }

    [Fact]
    public async Task TestGetVulnerabilitiesRiskNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetVulnerabilitiesAsync(404));
        Assert.Throws<DataNotFoundException>(() => _svc.GetVulnerabilities(404));
    }

    [Fact]
    public async Task TestGetFilteredVulnerabilities()
    {
        SeedLookups();
        Seed(ctx =>
        {
            var risk = NewRisk(1);
            risk.Vulnerabilities.Add(new Vulnerability { Id = 1, Status = (int)IntStatus.Active, Title = "V1" });
            ctx.Risks.Add(risk);
        });

        var (count, list) = await _svc.GetFilteredVulnerabilitiesAsync(1, new SieveModel());

        Assert.Equal(1, count);
        Assert.Single(list);
    }

    [Fact]
    public async Task TestIncidentResponsePlanAssociation()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan { Id = 1, Name = "IRP", Description = "" });
        });

        await _svc.AssocianteRiskToIncidentResponsePlanAsync(1, 1);
        var irp = await _svc.GetIncidentResponsePlanAsync(1);

        Assert.NotNull(irp);
        Assert.Equal(1, irp!.Id);
    }

    [Fact]
    public async Task TestIncidentResponsePlanAssociationNotFound()
    {
        SeedLookups();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.AssocianteRiskToIncidentResponsePlanAsync(1, 99));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.AssocianteRiskToIncidentResponsePlanAsync(99, 1));
    }

    [Fact]
    public void TestGetRisksNeedingReview()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "Open"));
            ctx.Risks.Add(NewRisk(2, status: "Open"));
            ctx.MgmtReviews.Add(new MgmtReview { Id = 1, RiskId = 1, Review = 1, Reviewer = 1, NextStep = 1, Comments = "" });
        });

        var needing = _svc.GetRisksNeedingReview();
        var needingOpen = _svc.GetRisksNeedingReview("Open");

        Assert.Single(needing);          // risk 2 has no review
        Assert.Single(needingOpen);
        Assert.Equal(2, needing[0].Id);
    }
}
