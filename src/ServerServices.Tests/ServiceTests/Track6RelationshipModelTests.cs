using System;
using System.Linq;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// DB-free assertions over the EF model metadata for the Track 6.3 relationships. Builds the NRDbContext model
/// with the real (Pomelo MySQL) provider — no connection is opened — and verifies each new correlation FK is a
/// HasOne relationship to <c>user</c> with <see cref="DeleteBehavior.SetNull"/> over a nullable FK column, and
/// that the incident&lt;-&gt;IRP join entity is represented exactly once.
/// </summary>
public class Track6RelationshipModelTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<NRDbContext>()
            .UseMySql("server=localhost;database=none;uid=x;pwd=x", ServerVersion.Parse("10.11.0-mariadb"))
            .Options;
        using var ctx = new AuditableContext(options);
        return ctx.Model;
    }

    [Theory]
    [InlineData(typeof(Risk), nameof(Risk.OwnerUser), "Owner")]
    [InlineData(typeof(Risk), nameof(Risk.ManagerUser), "Manager")]
    [InlineData(typeof(Risk), nameof(Risk.SubmittedByUser), "SubmittedBy")]
    [InlineData(typeof(FrameworkControl), nameof(FrameworkControl.ControlOwnerUser), "ControlOwner")]
    [InlineData(typeof(FrameworkControlTest), nameof(FrameworkControlTest.TesterUser), "Tester")]
    [InlineData(typeof(Incident), nameof(Incident.ReportedByUser), "ReportedById")]
    public void CorrelationNavigation_IsHasOneUser_SetNull_NullableFk(Type clrType, string navName, string fkProperty)
    {
        var model = BuildModel();
        var entity = model.FindEntityType(clrType)!;

        var nav = entity.FindNavigation(navName);
        Assert.NotNull(nav);

        var fk = nav!.ForeignKey;
        Assert.Equal(typeof(User), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);

        var prop = Assert.Single(fk.Properties);
        Assert.Equal(fkProperty, prop.Name);
        Assert.True(prop.IsNullable, $"{clrType.Name}.{fkProperty} must be nullable for ON DELETE SET NULL");
    }

    [Fact]
    public void IncidentToIncidentResponsePlan_IsMappedExactlyOnce()
    {
        var model = BuildModel();

        var joinEntity = model.FindEntityType(typeof(IncidentToIncidentResponsePlan));
        Assert.NotNull(joinEntity);
        Assert.Equal("incident_to_incident_response_plan", joinEntity!.GetTableName());

        // No duplicate/ambiguous mapping: exactly one entity type backs that join table.
        var backingThatTable = model.GetEntityTypes()
            .Count(e => e.GetTableName() == "incident_to_incident_response_plan");
        Assert.Equal(1, backingThatTable);
    }
}
