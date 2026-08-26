using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DAL.Context;

/// <summary>
/// Design-time context for <c>dotnet ef</c>.
///
/// Without this, EF builds the startup project's host to find a context, and any unrelated
/// dependency-injection problem in <c>ConsoleClient</c> — or the absence of a reachable database,
/// because that host resolves its server version with <c>ServerVersion.AutoDetect</c> — makes
/// <c>migrationAdd.sh</c> fail with an error that has nothing to do with the model. EF prefers a
/// design-time factory over the host, so declaring one makes migration authoring depend on the
/// model and nothing else.
///
/// The version is parsed rather than detected and the connection string is never opened: adding a
/// migration is a pure model operation. Set <c>NETRISK_DESIGN_CONNECTION</c> if a real connection is
/// wanted for <c>dotnet ef database update</c>; the placeholder is enough for everything else.
/// </summary>
public class NRDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NRDbContext>
{
    private const string PlaceholderConnection =
        "server=localhost;port=3306;database=netrisk;uid=netrisk;pwd=netrisk";

    public NRDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("NETRISK_DESIGN_CONNECTION")
                         ?? PlaceholderConnection;

        var optionsBuilder = new DbContextOptionsBuilder<NRDbContext>();
        optionsBuilder.UseMySql(connection, ServerVersion.Parse("10.11.0-mariadb"));

        return new NRDbContext(optionsBuilder.Options);
    }
}
