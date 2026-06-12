using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class RisksGap3InMemoryTest : InMemoryServiceTestBase
{
    private readonly IRisksService _svc;
    public RisksGap3InMemoryTest() => _svc = GetService<IRisksService>();

    private static Risk NewRisk(int id, string status = "Open", int owner = 7) => new()
    {
        Id = id, Status = status, Subject = $"R{id}", ReferenceId = "R", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", Source = 1, Category = 1, Owner = owner, Manager = owner, SubmittedBy = owner
    };

    private void SeedScopedUser() => Seed(ctx =>
    {
        ctx.Sources.Add(new Source { Value = 1, Name = "S" });
        ctx.Categories.Add(new Category { Value = 1, Name = "C" });
        ctx.Roles.Add(new Role { Value = 1, Name = "R" });   // no modify_risks permission
        ctx.Users.Add(new User { Value = 7, Name = "U", Type = "local", Enabled = true, RoleId = 1, Email = "u@x.io", Password = new byte[] { 1 } });
        ctx.Risks.Add(NewRisk(1, "Open", owner: 7));
        ctx.Risks.Add(NewRisk(2, "InProgress", owner: 7));
    });

    [Fact]
    public void TestGetUserRisksNonAdminAllStatusBranches()
    {
        SeedScopedUser();
        var user = new User { Value = 7, Admin = false, RoleId = 1 };

        Assert.NotNull(_svc.GetUserRisks(user, "Open", "Closed"));   // status && notStatus
        Assert.NotNull(_svc.GetUserRisks(user, "Open", null));       // status only
        Assert.NotNull(_svc.GetUserRisks(user, null, "Closed"));     // notStatus only
        Assert.NotNull(_svc.GetUserRisks(user, null, null));         // neither
    }

    [Fact]
    public void TestEntityAssociationMissingRiskThrows()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.CleanRiskEntityAssociations(999));
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteEntityAssociation(999, 1));
    }

    [Fact]
    public void TestSaveRiskWithCatalogs()
    {
        Seed(ctx =>
        {
            ctx.Sources.Add(new Source { Value = 1, Name = "S" });
            ctx.Categories.Add(new Category { Value = 1, Name = "C" });
            ctx.RiskCatalogs.Add(new RiskCatalog { Id = 5, Number = "5", Name = "Cat", Description = "d" });
            ctx.Risks.Add(NewRisk(1));
        });

        var update = NewRisk(1, status: "Updated");
        update.RiskCatalogs = new List<RiskCatalog> { new() { Id = 5 } };
        _svc.SaveRisk(update);

        Assert.Equal("Updated", _svc.GetRisk(1).Status);
        // missing catalog id throws
        var bad = NewRisk(1);
        bad.RiskCatalogs = new List<RiskCatalog> { new() { Id = 999 } };
        Assert.Throws<DataNotFoundException>(() => _svc.SaveRisk(bad));
    }
}

public class StatisticsGap3InMemoryTest : InMemoryServiceTestBase
{
    private readonly IStatisticsService _svc;
    public StatisticsGap3InMemoryTest() => _svc = GetService<IStatisticsService>();

    private static Risk NewRisk(int id, int? mitigationId = null) => new()
    {
        Id = id, Status = "Open", Subject = $"R{id}", ReferenceId = "R", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", MitigationId = mitigationId,
        SubmissionDate = DateTime.Now.AddDays(-1), LastUpdate = DateTime.Now.AddDays(-1)
    };

    private static Mitigation NewMitigation(int id, int cost) => new()
    {
        Id = id, RiskId = 0, MitigationCost = cost, CurrentSolution = "", SecurityRequirements = "",
        SecurityRecommendations = "", SubmissionDate = DateTime.Now, LastUpdate = DateTime.Now,
        PlanningDate = new DateOnly(2026, 1, 1)
    };

    [Fact]
    public void TestRisksVsCostsWithData()
    {
        Seed(ctx =>
        {
            ctx.MitigationCosts.Add(new MitigationCost { Value = 2, Name = "Low" });
            ctx.Mitigations.Add(NewMitigation(1, cost: 2));
            ctx.Risks.Add(NewRisk(1, mitigationId: 1));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 5f, ContributingScore = 1 });
        });

        var points = _svc.GetRisksVsCosts(0, 10);

        Assert.Single(points);
    }

    [Fact]
    public void TestEntitiesRiskValuesWithParentAndChildren()
    {
        Seed(ctx =>
        {
            var parent = new Entity { Id = 1, DefinitionName = "organization", DefinitionVersion = "1", Status = "active",
                Created = DateTime.Now, Updated = DateTime.Now };
            parent.EntitiesProperties.Add(new EntitiesProperty { Id = 1, Entity = 1, Type = "name", Value = "Parent", OldValue = "", Name = "name" });

            var child = new Entity { Id = 2, DefinitionName = "person", DefinitionVersion = "1", Status = "active",
                Parent = 1, Created = DateTime.Now, Updated = DateTime.Now };
            child.EntitiesProperties.Add(new EntitiesProperty { Id = 2, Entity = 2, Type = "name", Value = "Child", OldValue = "", Name = "name" });
            var risk = new Risk { Id = 1, Status = "Open", Subject = "R", ReferenceId = "R", Assessment = "", Notes = "",
                RiskCatalogMapping = "", ThreatCatalogMapping = "" };
            child.Risks.Add(risk);

            ctx.Entities.Add(parent);
            ctx.Entities.Add(child);
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 4f });
        });

        var byParent = _svc.GetEntitiesRiskValues(parentId: 1);
        var all = _svc.GetEntitiesRiskValues();

        Assert.NotNull(byParent);
        Assert.NotEmpty(all);
    }
}

public class RiskCalcGap3InMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskCalculationService _svc;
    public RiskCalcGap3InMemoryTest() => _svc = GetService<IRiskCalculationService>();

    [Fact]
    public async Task TestCalculateRiskScoreCreatesScoringWhenMissing()
    {
        Seed(ctx => ctx.Risks.Add(new Risk
        {
            Id = 1, Status = "Open", Subject = "R", ReferenceId = "R", Assessment = "", Notes = "",
            RiskCatalogMapping = "", ThreatCatalogMapping = ""
        }));

        // No RiskScoring seeded → exercises the new-scoring branch.
        await _svc.CalculateRiskScoreAsync();
    }
}

public class LinksGap3InMemoryTest : InMemoryServiceTestBase
{
    private readonly ILinksService _svc;
    public LinksGap3InMemoryTest() => _svc = GetService<ILinksService>();

    [Fact]
    public void TestGetLinkData()
    {
        var url = _svc.CreateLink("reset", DateTime.Now.AddDays(1), new byte[] { 9, 8, 7 });
        var key = url.Split("key=")[1];

        var data = _svc.GetLinkData("reset", key);

        Assert.Equal(new byte[] { 9, 8, 7 }, data);
        Assert.Throws<DataNotFoundException>(() => _svc.GetLinkData("reset", "wrong"));
    }
}
