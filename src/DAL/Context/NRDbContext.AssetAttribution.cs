using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// M58, T281–T282 (S39 §3.4, §8.2, D-14, D-18, D-20, D-21) — the asset data the risk index read with a workaround,
/// corrected at the source: a scan-only assessment date and the writer of a host's entity, the per-connection re-stamp
/// policy, and the repository of a code-scanner import and its findings. Track 6 naming throughout; columns on tables
/// already filtered (<c>hosts</c>, <c>vulnerabilities</c>) or unscoped by design, so no new filter.
/// </summary>
public partial class NRDbContext
{
    private static void ConfigureAssetAttribution(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Host>(entity =>
        {
            entity.Property(e => e.LastAssessedAt).HasColumnName("last_assessed_at").HasColumnType("datetime");
            entity.Property(e => e.EntitySource).HasColumnName("entity_source").HasColumnType("varchar(32)")
                .HasMaxLength(32);
        });

        modelBuilder.Entity<ScanImport>(entity =>
        {
            entity.Property(e => e.Repository).HasColumnName("repository").HasColumnType("varchar(255)")
                .HasMaxLength(255);
        });

        modelBuilder.Entity<Vulnerability>(entity =>
        {
            entity.Property(e => e.Repository).HasColumnName("repository").HasColumnType("varchar(255)")
                .HasMaxLength(255);
        });

        modelBuilder.Entity<TrendMicroConnection>(entity =>
        {
            entity.Property(e => e.EntityRestampPolicy).HasColumnName("entity_restamp_policy").HasColumnType("int(11)")
                .HasConversion<int>().HasDefaultValue(RestampPolicy.OnlyUnassigned);
        });

        modelBuilder.Entity<IssueTrackerConnection>(entity =>
        {
            entity.Property(e => e.EntityRestampPolicy).HasColumnName("entity_restamp_policy").HasColumnType("int(11)")
                .HasConversion<int>().HasDefaultValue(RestampPolicy.OnlyUnassigned);
        });
    }
}
