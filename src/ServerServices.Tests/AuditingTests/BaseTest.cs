using System;
using DAL.Auditing;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ServerServices.Tests.AuditingTests;

[TestSubject(typeof(Base))]
public class BaseTest
{
    [Fact]
    public void TestNewInstanceHasGeneratedId()
    {
        var a = new Base();
        var b = new Base();

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void TestDefaultDatesAreNull()
    {
        var entity = new Base();

        Assert.Null(entity.CreatedAt);
        Assert.Null(entity.UpdatedAt);
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Detached)]
    public void TestUpdateDateSetsCreatedAt(EntityState state)
    {
        var entity = new Base();

        entity.UpdateDate(state);

        Assert.NotNull(entity.CreatedAt);
        Assert.Null(entity.UpdatedAt);
    }

    [Fact]
    public void TestUpdateDateModifiedSetsUpdatedAt()
    {
        var entity = new Base();

        entity.UpdateDate(EntityState.Modified);

        Assert.NotNull(entity.UpdatedAt);
        Assert.Null(entity.CreatedAt);
    }

    [Theory]
    [InlineData(EntityState.Deleted)]
    [InlineData(EntityState.Unchanged)]
    public void TestUpdateDateNonTrackedStatesLeaveDatesUntouched(EntityState state)
    {
        var entity = new Base();

        entity.UpdateDate(state);

        Assert.Null(entity.CreatedAt);
        Assert.Null(entity.UpdatedAt);
    }

    [Fact]
    public void TestPropertiesAreSettable()
    {
        var id = Guid.NewGuid();
        var created = new DateTime(2026, 1, 1);
        var updated = new DateTime(2026, 2, 2);

        var entity = new Base { Id = id, CreatedAt = created, UpdatedAt = updated };

        Assert.Equal(id, entity.Id);
        Assert.Equal(created, entity.CreatedAt);
        Assert.Equal(updated, entity.UpdatedAt);
    }
}
