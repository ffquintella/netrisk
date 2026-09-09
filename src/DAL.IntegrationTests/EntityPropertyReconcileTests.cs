using DAL;
using DAL.Context;
using DAL.Entities;
using Model.Entities;
using MySqlConnector;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Covers <see cref="EntitiesService.ReplaceProperties"/> against a real MariaDB, because the three
/// defects it replaces were all invisible to the EF in-memory provider: the identity conflict needs
/// EF's real change tracker over a relational store, and the stale-id failure needs a real
/// auto-increment column handing out new ids after a delete.
///
/// The fixture data is the row that actually failed — the "Verificação de Conformidade"
/// businessProcess (dev entity 83), whose save returned 500 with "Error updating entities".
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class EntityPropertyReconcileTests(MariaDbContainerFixture fixture)
{
    private const int BusinessProcess = 83;
    private const int OrgUnitDci = 82;
    private const int OrgUnitOther = 84;
    private const int AppOne = 90;
    private const int AppTwo = 91;

    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
            f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private IEntitiesService NewService() =>
        new EntitiesService(Substitute.For<Serilog.ILogger>(), new ContainerDal(fixture));

    private async Task SeedAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(83);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        async Task Exec(string sql)
        {
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await Exec("DELETE FROM entities_properties;");
        await Exec("DELETE FROM entities;");
        await Exec(@"INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES
                     (63,'organization','1.3','2023-11-10 17:21:00','2023-11-10 17:21:00',52,52,'active',NULL),
                     (82,'organizationUnit','1.3','2023-11-10 17:21:54','2023-11-10 14:21:55',52,52,'active',63),
                     (84,'organizationUnit','1.3','2023-11-10 17:21:54','2023-11-10 14:21:55',52,52,'active',63),
                     (90,'application','1.3','2023-11-10 17:21:54','2023-11-10 14:21:55',52,52,'active',82),
                     (91,'application','1.3','2023-11-10 17:21:54','2023-11-10 14:21:55',52,52,'active',82),
                     (83,'businessProcess','1.3','2023-11-10 17:22:23','2023-11-10 14:22:23',52,52,'active',82);");
        await Exec(@"INSERT INTO entities_properties (Id, Type, Value, OldValue, Entity, Name) VALUES
                     (220,'name','Verificação de Conformidade','Verificação de Conformidade',83,'name-83'),
                     (369,'description','teste','',83,'description-83'),
                     (370,'objective','teste','',83,'objective-83'),
                     (371,'isActive','true','',83,'isActive-83'),
                     (221,'organizationUnit','82','82',83,'organizationUnit-83-82');");
    }

    /// <summary>
    /// The rows GUIClient's EntityFormViewModel emits for that entity. Every row of a multi-valued
    /// field carries the same PropertyId, which is exactly the id the reconcile must not trust.
    /// </summary>
    private static List<EntitiesPropertyDto> Payload(
        string name = "Verificação de Conformidade",
        string description = "teste",
        int[]? organizationUnits = null,
        int[]? applications = null,
        int organizationUnitPropertyId = 221,
        int applicationsPropertyId = 0)
    {
        var properties = new List<EntitiesPropertyDto>
        {
            new() { Id = 220, Type = "name", Value = name, Name = "name-83" },
            new() { Id = 369, Type = "description", Value = description, Name = "description-83" },
            new() { Id = 370, Type = "objective", Value = "teste", Name = "objective-83" },
            new() { Id = 371, Type = "isActive", Value = "true", Name = "isActive-83" },
        };

        foreach (var unit in organizationUnits ?? [OrgUnitDci])
            properties.Add(new EntitiesPropertyDto
            {
                Id = organizationUnitPropertyId, Type = "organizationUnit",
                Value = unit.ToString(), Name = $"organizationUnit-83-{unit}"
            });

        foreach (var application in applications ?? [])
            properties.Add(new EntitiesPropertyDto
            {
                Id = applicationsPropertyId, Type = "applications",
                Value = application.ToString(), Name = $"applications-83-{application}"
            });

        return properties;
    }

    /// <summary>EntitiesController.Update's persistence steps, minus the HTTP concerns.</summary>
    private static void Save(IEntitiesService svc, List<EntitiesPropertyDto> properties)
    {
        var entity = svc.GetEntity(BusinessProcess);
        entity.Updated = DateTime.Now;
        entity.UpdatedBy = 52;
        entity.Status = "active";
        entity.Parent = OrgUnitDci;

        svc.ValidatePropertyList(entity.DefinitionName, properties);
        svc.ReplaceProperties(entity, properties);
        svc.UpdateEntity(entity);
    }

    private async Task<List<(int Id, string Type, string Value, string OldValue)>> RowsAsync()
    {
        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT Id, Type, Value, OldValue FROM entities_properties WHERE Entity = 83 ORDER BY Type, Value", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(int, string, string, string)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    [Fact]
    public async Task Saving_An_Unchanged_Entity_Keeps_Every_Row_And_Its_Id()
    {
        await SeedAsync();

        Save(NewService(), Payload());

        var rows = await RowsAsync();
        Assert.Equal(5, rows.Count);
        Assert.Equal(221, rows.Single(r => r.Type == "organizationUnit").Id);
        Assert.Equal(220, rows.Single(r => r.Type == "name").Id);
    }

    /// <summary>
    /// The failure in the bug report. One newly selected application arrives as a single row, which
    /// the old code routed through CreateProperty and *then* appended to the entity a second time;
    /// UpdateEntity's Adapt turned the duplicate into two EF instances sharing a key and SaveChanges
    /// threw "another instance with the same key value for {'Id'} is already being tracked" — a 500.
    /// </summary>
    [Fact]
    public async Task Adding_A_Single_Application_Persists_It()
    {
        await SeedAsync();

        Save(NewService(), Payload(applications: [AppOne]));

        var rows = await RowsAsync();
        Assert.Equal("90", rows.Single(r => r.Type == "applications").Value);
    }

    [Fact]
    public async Task Adding_Two_Applications_Persists_Both()
    {
        await SeedAsync();

        Save(NewService(), Payload(applications: [AppOne, AppTwo]));

        var rows = await RowsAsync();
        Assert.Equal(["90", "91"], rows.Where(r => r.Type == "applications").Select(r => r.Value));
    }

    /// <summary>
    /// Saving twice from the same open form. The view model still holds the property id it read when
    /// the form loaded, and the old update path had deleted that row and inserted a replacement with
    /// a fresh auto-increment id — so the second save asked to update a row that no longer existed
    /// and failed with "EntityProperty not found".
    /// </summary>
    [Fact]
    public async Task Saving_Twice_From_The_Same_Form_Succeeds()
    {
        await SeedAsync();
        var svc = NewService();

        Save(svc, Payload(organizationUnits: [OrgUnitDci, OrgUnitOther]));
        Save(svc, Payload(organizationUnits: [OrgUnitDci]));

        var rows = await RowsAsync();
        Assert.Equal("82", rows.Single(r => r.Type == "organizationUnit").Value);
    }

    /// <summary>An id that no longer exists is data the reconcile must ignore, not trust.</summary>
    [Fact]
    public async Task A_Stale_Property_Id_Is_Ignored()
    {
        await SeedAsync();

        Save(NewService(), Payload(organizationUnitPropertyId: 9999));

        var rows = await RowsAsync();
        Assert.Equal(221, rows.Single(r => r.Type == "organizationUnit").Id);
    }

    /// <summary>
    /// Clearing a multi-valued property. The old code inferred multi-valuedness from the number of
    /// rows in the payload, so a cleared field — which emits no rows at all — was skipped and its
    /// rows stayed in the database for good.
    /// </summary>
    [Fact]
    public async Task Clearing_A_Multi_Valued_Property_Removes_Its_Rows()
    {
        await SeedAsync();
        var svc = NewService();

        Save(svc, Payload(applications: [AppOne, AppTwo]));
        Assert.Equal(2, (await RowsAsync()).Count(r => r.Type == "applications"));

        Save(svc, Payload(applications: []));

        Assert.DoesNotContain("applications", (await RowsAsync()).Select(r => r.Type));
    }

    /// <summary>
    /// Legacy drift: the old delete-then-recreate path could leave two rows of one multi-valued
    /// type holding the same value. Reconciling collapses them instead of preserving the duplicate.
    /// </summary>
    [Fact]
    public async Task Duplicate_Rows_For_One_Value_Are_Collapsed()
    {
        await SeedAsync();

        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO entities_properties (Id, Type, Value, OldValue, Entity, Name) " +
                "VALUES (900,'organizationUnit','82','',83,'organizationUnit-83-82');", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        Save(NewService(), Payload());

        var rows = await RowsAsync();
        Assert.Equal(221, rows.Single(r => r.Type == "organizationUnit").Id);
    }

    [Fact]
    public async Task Changing_A_Single_Valued_Property_Keeps_The_Row_And_Records_The_Old_Value()
    {
        await SeedAsync();

        Save(NewService(), Payload(description: "teste2"));

        var description = (await RowsAsync()).Single(r => r.Type == "description");
        Assert.Equal(369, description.Id);
        Assert.Equal("teste2", description.Value);
        Assert.Equal("teste", description.OldValue);
    }

    [Fact]
    public async Task Two_Values_For_A_Single_Valued_Property_Are_Refused()
    {
        await SeedAsync();
        var svc = NewService();

        var payload = Payload();
        payload.Add(new EntitiesPropertyDto { Id = 220, Type = "name", Value = "Outro", Name = "name-83" });

        var ex = Assert.Throws<Exception>(() => Save(svc, payload));
        Assert.Contains("single value", ex.Message);

        // and nothing was written
        Assert.Equal("Verificação de Conformidade", (await RowsAsync()).Single(r => r.Type == "name").Value);
    }

    /// <summary>
    /// A required property missing from the payload is a malformed request, not an instruction to
    /// clear it — the controller's ValidatePropertyList call is what keeps "absent means cleared"
    /// safe for the nullable properties.
    /// </summary>
    [Fact]
    public async Task A_Payload_Missing_A_Required_Property_Is_Refused()
    {
        await SeedAsync();
        var svc = NewService();

        var payload = Payload();
        payload.RemoveAll(p => p.Type == "organizationUnit");

        var ex = Assert.Throws<Exception>(() => Save(svc, payload));
        Assert.Contains("organizationUnit is required", ex.Message);
        Assert.Equal("82", (await RowsAsync()).Single(r => r.Type == "organizationUnit").Value);
    }
}
