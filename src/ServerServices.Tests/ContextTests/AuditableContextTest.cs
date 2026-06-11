using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ServerServices.Tests.ContextTests;

[TestSubject(typeof(AuditableContext))]
public class AuditableContextTest
{
    private static AuditableContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuditableContext(options) { UserId = 42 };
    }

    private static Comment CreateComment() => new()
    {
        Text = "Test",
        Date = new DateTime(2026, 1, 1),
        IsAnonymous = false,
        CommenterName = "Tester"
    };

    [Fact]
    public async Task TestSaveChangesAsyncCreatesCreateAudit()
    {
        // Arrange
        await using var context = CreateContext();
        var comment = CreateComment();
        await context.Comments.AddAsync(comment);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var audit = Assert.Single(context.Audits.ToList());
        Assert.Equal("Create", audit.Type);
        Assert.Equal(nameof(Comment), audit.TableName);
        Assert.Equal(42, audit.UserId);
        Assert.NotNull(audit.NewValues);
        Assert.Contains("Test", audit.NewValues);
        Assert.Null(audit.OldValues);
        Assert.NotNull(audit.PrimaryKey);
        Assert.Contains("Id", audit.PrimaryKey);
    }

    [Fact]
    public async Task TestSaveChangesAsyncCreatesUpdateAudit()
    {
        // Arrange
        await using var context = CreateContext();
        var comment = CreateComment();
        await context.Comments.AddAsync(comment);
        await context.SaveChangesAsync();

        // Act
        comment.Text = "Changed";
        await context.SaveChangesAsync();

        // Assert
        var audit = Assert.Single(context.Audits.Where(a => a.Type == "Update").ToList());
        Assert.Equal(nameof(Comment), audit.TableName);
        Assert.Contains(nameof(Comment.Text), audit.AffectedColumns);
        Assert.NotNull(audit.OldValues);
        Assert.Contains("Test", audit.OldValues);
        Assert.NotNull(audit.NewValues);
        Assert.Contains("Changed", audit.NewValues);
    }

    [Fact]
    public async Task TestSaveChangesAsyncCreatesDeleteAudit()
    {
        // Arrange
        await using var context = CreateContext();
        var comment = CreateComment();
        await context.Comments.AddAsync(comment);
        await context.SaveChangesAsync();

        // Act
        context.Comments.Remove(comment);
        await context.SaveChangesAsync();

        // Assert
        var audit = Assert.Single(context.Audits.Where(a => a.Type == "Delete").ToList());
        Assert.Equal(nameof(Comment), audit.TableName);
        Assert.NotNull(audit.OldValues);
        Assert.Contains("Test", audit.OldValues);
        Assert.Null(audit.NewValues);
    }

    [Fact]
    public async Task TestUnchangedEntityProducesNoAudit()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Comments.AddAsync(CreateComment());
        await context.SaveChangesAsync();
        var auditsAfterCreate = context.Audits.Count();

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(auditsAfterCreate, context.Audits.Count());
    }

    [Fact]
    public async Task TestAuditEntityIsNotAudited()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Audits.AddAsync(new Audit
        {
            UserId = 1,
            Type = "Create",
            TableName = "Manual",
            DateTime = new DateTime(2026, 1, 1),
            AffectedColumns = ""
        });

        // Act
        await context.SaveChangesAsync();

        // Assert
        var audit = Assert.Single(context.Audits.ToList());
        Assert.Equal("Manual", audit.TableName);
    }

    [Fact]
    public void TestSaveChangesCreatesAudit()
    {
        // Arrange
        using var context = CreateContext();
        context.Comments.Add(CreateComment());

        // Act
        var result = context.SaveChanges();

        // Assert
        Assert.True(result > 0);
        var audit = Assert.Single(context.Audits.ToList());
        Assert.Equal("Create", audit.Type);
        Assert.Equal(nameof(Comment), audit.TableName);
    }

    [Fact]
    public async Task TestAuditUserIdDefaultsToZero()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AuditableContext(options);
        await context.Comments.AddAsync(CreateComment());

        // Act
        await context.SaveChangesAsync();

        // Assert
        var audit = Assert.Single(context.Audits.ToList());
        Assert.Equal(0, audit.UserId);
    }

    [Fact]
    public async Task TestMultipleEntitiesProduceOneAuditEach()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Comments.AddAsync(CreateComment());
        await context.Comments.AddAsync(CreateComment());

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, context.Audits.Count(a => a.Type == "Create"));
    }
}
