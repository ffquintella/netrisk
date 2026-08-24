using System.Reflection;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Guards the model against the one property shape that makes EF Core 10 + Pomelo fail to build a
/// relational model at all.
///
/// A <see cref="string"/> property whose store type is <c>char(n)</c> is a string, and a string is an
/// <c>IEnumerable&lt;char&gt;</c>. EF Core 10's <c>ElementMappingConvention</c> therefore treats it as
/// a primitive collection of <c>char</c> and asks the provider for a char element mapping, which the
/// MySQL provider does not have — the lookup returns null and the convention dereferences it. The
/// resulting <c>NullReferenceException</c> surfaces from deep inside the type mapping source with no
/// mention of the offending property, and it takes down <c>dotnet ef migrations script</c>, the
/// schema-consistency guard and anything else that builds the model.
///
/// It is easy to re-introduce by accident, because <c>HasMaxLength(n).IsFixedLength()</c> looks
/// innocuous in <c>OnModelCreating</c> and only becomes <c>HasColumnType("char(n)")</c> when the model
/// snapshot is regenerated — so the failure appears one <c>migrationAdd.sh</c> later, in a file nobody
/// hand-edited. These tests fail immediately instead, naming the property.
///
/// <c>Guid</c> columns are unaffected and deliberately not flagged: Pomelo maps them to <c>char(36)</c>
/// too, but a <c>Guid</c> is not a collection of anything.
/// </summary>
public class StringColumnTypeGuardTest
{
    private static NRDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NRDbContext>()
            // Parse rather than AutoDetect, so no connection is opened.
            .UseMySql("server=unused;database=netrisk;user=x;password=y", ServerVersion.Parse("10.11.0-mariadb"))
            .Options;

        return new NRDbContext(options);
    }

    /// <summary>
    /// The runtime model, as every tier builds it.
    /// </summary>
    [Fact]
    public void NoStringPropertyUsesAFixedLengthCharColumn()
    {
        IModel model;

        try
        {
            using var context = BuildContext();
            // Touching Model runs the finalizing conventions, which is where a char(n) string blows
            // up — so the offending property cannot be enumerated once it is there. Catching the
            // failure to explain it is the whole point: the raw exception names nothing.
            model = context.Model;
        }
        catch (Exception ex)
        {
            Assert.Fail(
                "The EF model failed to build. The usual cause is a string property whose store type " +
                "is char(n): EF Core 10 treats it as a primitive collection of char and the MySQL " +
                "provider has no char element mapping, so the failure is a NullReferenceException " +
                "that names nothing. Use varchar(n) instead. " +
                $"Underlying error: {ex}");
            return;
        }

        AssertNoCharStrings(model, "the EF model (NRDbContext.OnModelCreating)");
    }

    /// <summary>
    /// The generated snapshot, which is the copy that actually breaks: it re-resolves store types, so
    /// a property expressed as max-length + fixed-length in <c>OnModelCreating</c> still lands here as
    /// <c>char(n)</c>.
    /// </summary>
    [Fact]
    public void TheModelSnapshotUsesNoFixedLengthCharColumnEither()
    {
        AssertNoCharStrings(SnapshotModel(), "the generated model snapshot (NRDbContextModelSnapshot)");
    }

    /// <summary>
    /// The end state the two tests above exist to protect: the snapshot's relational model builds.
    /// <c>HasPendingModelChanges</c>, <c>dotnet ef migrations script</c> and <c>database update</c> all
    /// need this, and all of them fail with the same opaque null reference when it does not.
    /// </summary>
    [Fact]
    public void TheModelSnapshotCanBuildItsRelationalModel()
    {
        var exception = Record.Exception(() => FinalizedSnapshotModel().GetRelationalModel());

        Assert.True(exception is null,
            "The model snapshot cannot build a relational model, so `dotnet ef migrations script` and " +
            "the schema-consistency guard will both fail. The usual cause is a string column whose " +
            $"store type is char(n) — see the other tests in this class. Underlying error: {exception}");
    }

    private static void AssertNoCharStrings(IModel model, string where)
    {
        var offenders = new List<string>();

        foreach (var entityType in model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
        {
            if (property.ClrType != typeof(string)) continue;

            var columnType = property.GetColumnType();
            if (columnType is null) continue;

            if (columnType.StartsWith("char(", StringComparison.OrdinalIgnoreCase) ||
                columnType.Equals("char", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{entityType.DisplayName()}.{property.Name} -> {columnType}");
        }

        Assert.True(offenders.Count == 0,
            $"These string properties resolve to a char(n) column in {where}, which makes EF Core 10 " +
            "treat them as primitive collections of char and the model build fail with a " +
            "NullReferenceException. Use varchar(n) instead — the two hold the same value, differing " +
            "only in trailing-space padding.\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The snapshot model as written. Loaded by reflection because the generated class is internal.
    ///
    /// This is the model straight out of <c>BuildModel</c>: good enough to read store types from, but
    /// not yet finalized, so it cannot build a relational model. Use
    /// <see cref="FinalizedSnapshotModel"/> for that.
    /// </summary>
    private static IModel SnapshotModel()
    {
        var snapshotType = typeof(NRDbContext).Assembly.GetType("DAL.Migrations.NRDbContextModelSnapshot")
                           ?? throw new InvalidOperationException("NRDbContextModelSnapshot was not found.");

        var snapshot = Activator.CreateInstance(snapshotType)!;

        return (IModel)snapshotType.GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(snapshot)!;
    }

    /// <summary>
    /// The snapshot model put through the same finalize-and-initialize pipeline EF runs before
    /// diffing it, which is where the char(n) failure actually surfaces. Skipping this step reads as
    /// a different error ("the model must be finalized") and hides the real one.
    /// </summary>
    private static IModel FinalizedSnapshotModel()
    {
        using var context = BuildContext();

        var model = SnapshotModel();
        var finalized = ((IMutableModel)model).FinalizeModel();

        return context.GetService<IModelRuntimeInitializer>().Initialize(finalized, designTime: true);
    }
}
