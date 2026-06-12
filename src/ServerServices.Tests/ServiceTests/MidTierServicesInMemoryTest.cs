using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using Model.Exceptions;
using Model.Risks;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class TeamsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly ITeamsService _svc;
    public TeamsServiceInMemoryTest() => _svc = GetService<ITeamsService>();

    [Fact]
    public void TestCreateGetAllGetByIdDelete()
    {
        var created = _svc.Create(new Team { Value = 1, Name = "T1" });
        Assert.Equal(1, created.Value);
        Assert.Single(_svc.GetAll());
        Assert.Equal("T1", _svc.GetById(1).Name);
        Assert.Throws<DataNotFoundException>(() => _svc.GetById(99));

        _svc.Delete(1);
        Assert.Empty(_svc.GetAll());
        Assert.Throws<DataNotFoundException>(() => _svc.Delete(1));
    }

    [Fact]
    public void TestUpdateTeamUsersAndGetUsersIds()
    {
        Seed(ctx =>
        {
            ctx.Teams.Add(new Team { Value = 1, Name = "T1" });
            ctx.Users.Add(new User { Value = 10, Name = "U", Type = "local", Email = "u@x.io", Password = new byte[] { 1 }, Enabled = true });
        });

        _svc.UpdateTeamUsers(1, new List<int> { 10 });

        Assert.Equal(new List<int> { 10 }, _svc.GetUsersIds(1));
        Assert.Throws<DataNotFoundException>(() => _svc.GetUsersIds(99));
        Assert.Throws<DataNotFoundException>(() => _svc.UpdateTeamUsers(99, new List<int>()));
    }

    [Fact]
    public void TestAssociateTeamToMitigationAndGetByMitigationId()
    {
        Seed(ctx =>
        {
            ctx.Teams.Add(new Team { Value = 1, Name = "T1" });
            ctx.Mitigations.Add(MitigationsServiceInMemoryTest.NewMitigation(5, riskId: 1));
        });

        _svc.AssociateTeamToMitigation(5, 1);

        Assert.Single(_svc.GetByMitigationId(5));
        Assert.Throws<DataNotFoundException>(() => _svc.AssociateTeamToMitigation(99, 1));
    }
}

public class MitigationsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IMitigationsService _svc;
    public MitigationsServiceInMemoryTest() => _svc = GetService<IMitigationsService>();

    internal static Mitigation NewMitigation(int id, int riskId = 1) => new()
    {
        Id = id, RiskId = riskId,
        CurrentSolution = "", SecurityRequirements = "", SecurityRecommendations = "",
        SubmissionDate = new DateTime(2026, 1, 1), LastUpdate = new DateTime(2026, 1, 1),
        PlanningDate = new DateOnly(2026, 1, 1)
    };

    private static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "Open", Subject = "S", ReferenceId = "R", Assessment = "",
        Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = ""
    };

    [Fact]
    public void TestCreateAndGetters()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        var created = _svc.Create(NewMitigation(0, riskId: 1));
        Assert.True(created.Id > 0);

        Assert.Equal(created.Id, _svc.GetById(created.Id).Id);
        Assert.Equal(created.Id, _svc.GetByRiskId(1).Id);
        Assert.Throws<DataNotFoundException>(() => _svc.GetById(999));
        Assert.Throws<DataNotFoundException>(() => _svc.GetByRiskId(999));
    }

    [Fact]
    public void TestCreateMissingRiskThrows()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.Create(NewMitigation(0, riskId: 404)));
    }

    [Fact]
    public void TestSave()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.Mitigations.Add(NewMitigation(1, riskId: 1));
        });

        var update = NewMitigation(1, riskId: 1);
        update.CurrentSolution = "Updated";
        _svc.Save(update);

        Assert.Equal("Updated", _svc.GetById(1).CurrentSolution);
        Assert.Throws<DataNotFoundException>(() => _svc.Save(NewMitigation(99)));
    }

    [Fact]
    public void TestListLookups()
    {
        Seed(ctx =>
        {
            ctx.PlanningStrategies.Add(new PlanningStrategy { Value = 1, Name = "S" });
            ctx.MitigationEfforts.Add(new MitigationEffort { Value = 1, Name = "E" });
            ctx.MitigationCosts.Add(new MitigationCost { Value = 1, Name = "C" });
        });

        Assert.Single(_svc.ListStrategies());
        Assert.Single(_svc.ListEfforts());
        Assert.Single(_svc.ListCosts());
    }

    [Fact]
    public void TestDeleteTeamsAssociations()
    {
        Seed(ctx =>
        {
            ctx.Mitigations.Add(NewMitigation(1));
            ctx.MitigationToTeams.Add(new MitigationToTeam { MitigationId = 1, TeamId = 1 });
        });

        _svc.DeleteTeamsAssociations(1);

        using var ctx = OpenContext();
        Assert.Empty(ctx.MitigationToTeams.ToList());
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteTeamsAssociations(99));
    }
}

public class SettingsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly ISettingsService _svc;
    public SettingsServiceInMemoryTest() => _svc = GetService<ISettingsService>();

    [Fact]
    public async System.Threading.Tasks.Task TestConfigurationKeyValueLifecycle()
    {
        Assert.False(await _svc.ConfigurationKeyExistsAsync("k1"));

        await _svc.SetConfigurationKeyValueAsync("k1", "v1");   // insert
        Assert.True(await _svc.ConfigurationKeyExistsAsync("k1"));
        Assert.Equal("v1", await _svc.GetConfigurationKeyValueAsync("k1"));

        await _svc.SetConfigurationKeyValueAsync("k1", "v2");   // update
        Assert.Equal("v2", await _svc.GetConfigurationKeyValueAsync("k1"));
    }

    [Fact]
    public async System.Threading.Tasks.Task TestGetMissingKeyThrows()
    {
        await Assert.ThrowsAsync<Exception>(() => _svc.GetConfigurationKeyValueAsync("missing"));
    }
}

public class ConfigurationsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IConfigurationsService _svc;
    public ConfigurationsServiceInMemoryTest() => _svc = GetService<IConfigurationsService>();

    [Fact]
    public void TestBackupPassword()
    {
        Assert.Equal("", _svc.GetBackupPassword());

        _svc.UpdateBackupPassword("secret");      // insert
        Assert.Equal("secret", _svc.GetBackupPassword());

        _svc.UpdateBackupPassword("secret2");     // update
        Assert.Equal("secret2", _svc.GetBackupPassword());
    }
}

public class ImpactsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IImpactsService _svc;
    public ImpactsServiceInMemoryTest() => _svc = GetService<IImpactsService>();

    [Fact]
    public void TestGetAll()
    {
        var impacts = _svc.GetAll();

        Assert.Equal(5, impacts.Count);
        Assert.Equal("Critical", impacts.Last().Value);
    }
}
