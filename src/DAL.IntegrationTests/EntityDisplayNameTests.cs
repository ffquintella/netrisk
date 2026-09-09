using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Covers <see cref="Entity.DisplayName"/> and the <see cref="object.ToString"/> override it backs.
///
/// The regression they fix was visible: the governance admin screen's entity pickers listed ten rows
/// all reading <c>DAL.Entities.Entity</c>, because a list control given no template falls back to
/// <c>ToString()</c> and an entity's name is not a column — it is a row in the property bag.
///
/// No container here: these are plain property assertions plus a model check that only needs the
/// model to be built, not a database to exist.
/// </summary>
public class EntityDisplayNameTests
{
    private static Entity EntityWithProperties(int id, params (string Type, string Value)[] properties)
    {
        var entity = new Entity { Id = id, DefinitionName = "organization", DefinitionVersion = "1", Status = "New" };

        foreach (var (type, value) in properties)
            entity.EntitiesProperties.Add(new EntitiesProperty { Entity = id, Type = type, Value = value });

        return entity;
    }

    [Fact]
    public void DisplayName_Returns_The_Name_Property()
    {
        var entity = EntityWithProperties(7, ("type", "organization"), ("name", "Finance"));

        Assert.Equal("Finance", entity.DisplayName);
        Assert.Equal("Finance", entity.ToString());
    }

    [Fact]
    public void DisplayName_Falls_Back_To_The_Id_When_The_Name_Property_Is_Absent()
    {
        // What an entity loaded without its property bag looks like. "#12" is wrong-ish but
        // selectable and traceable; an empty string would render a blank, unclickable list row.
        var entity = EntityWithProperties(12);

        Assert.Equal("#12", entity.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_Falls_Back_To_The_Id_When_The_Name_Is_Blank(string name)
    {
        var entity = EntityWithProperties(3, ("name", name));

        Assert.Equal("#3", entity.DisplayName);
    }

    [Fact]
    public void DisplayName_Is_Not_Mapped_To_A_Column()
    {
        // The property is computed from the bag, so a mapping would mean EF selecting — and on save,
        // writing — a column no migration ever created.
        var options = new DbContextOptionsBuilder<NRDbContext>()
            // Parse rather than AutoDetect, so no connection is opened.
            .UseMySql("server=unused;database=netrisk;user=x;password=y", ServerVersion.Parse("10.11.0-mariadb"))
            .Options;

        using var context = new NRDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(Entity));

        Assert.NotNull(entityType);
        Assert.Null(entityType!.FindProperty(nameof(Entity.DisplayName)));
    }
}
