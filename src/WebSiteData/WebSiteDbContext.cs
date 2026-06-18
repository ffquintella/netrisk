using Microsoft.EntityFrameworkCore;
using WebSiteData.Entities;

namespace WebSiteData;

public class WebSiteDbContext : DbContext
{
    public WebSiteDbContext(DbContextOptions<WebSiteDbContext> options) : base(options) { }

    public DbSet<LocalFixRequest> FixRequests => Set<LocalFixRequest>();
    public DbSet<LocalComment> Comments => Set<LocalComment>();
    public DbSet<LocalTeam> Teams => Set<LocalTeam>();
    public DbSet<LocalTeamUser> TeamUsers => Set<LocalTeamUser>();
    public DbSet<LocalUser> Users => Set<LocalUser>();
    public DbSet<LocalLink> Links => Set<LocalLink>();
    public DbSet<LocalIrpTaskExecution> IrpTaskExecutions => Set<LocalIrpTaskExecution>();
    public DbSet<LocalIrpTask> IrpTasks => Set<LocalIrpTask>();
    public DbSet<LocalIncident> Incidents => Set<LocalIncident>();
    public DbSet<OutboxItem> Outbox => Set<OutboxItem>();
    public DbSet<SyncState> SyncState => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalFixRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Identifier);
        });
        modelBuilder.Entity<LocalComment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FixRequestId);
        });
        modelBuilder.Entity<LocalTeam>().HasKey(x => x.Id);
        modelBuilder.Entity<LocalTeamUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TeamId);
        });
        modelBuilder.Entity<LocalUser>().HasKey(x => x.Value);
        modelBuilder.Entity<LocalLink>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyHash);
        });
        modelBuilder.Entity<LocalIrpTaskExecution>().HasKey(x => x.Id);
        modelBuilder.Entity<LocalIrpTask>().HasKey(x => x.Id);
        modelBuilder.Entity<LocalIncident>().HasKey(x => x.Id);
        modelBuilder.Entity<OutboxItem>(e =>
        {
            e.HasKey(x => x.ClientActionId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Status).HasConversion<int>();
        });
        modelBuilder.Entity<SyncState>().HasKey(x => x.Id);
    }
}
