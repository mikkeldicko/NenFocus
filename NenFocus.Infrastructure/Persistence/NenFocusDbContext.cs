using Microsoft.EntityFrameworkCore;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Infrastructure.Persistence;

public class NenFocusDbContext : DbContext
{
    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<BlockProfile> BlockProfiles { get; set; } = null!;
    public DbSet<PlannedBlock> PlannedBlocks { get; set; } = null!;
    public DbSet<BlockedAttempt> BlockedAttempts { get; set; } = null!;

    public NenFocusDbContext(DbContextOptions<NenFocusDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Session ────────────────────────────────────────────────────────
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.Property(s => s.CreatedAt).IsRequired();
            entity.Property(s => s.StartedAt);
            entity.Property(s => s.EndedAt);
            entity.Property(s => s.BreaksTaken).IsRequired().HasDefaultValue(0);
            entity.Property(s => s.BreakStartedAt);
            entity.Property(s => s.Reflection).HasMaxLength(2000);
            entity.Property(s => s.BlockedAttemptsSerialized).HasColumnName("BlockedAttempts");

            entity.Property(s => s.Goal)
                .HasConversion(goal => goal.Value, value => new SessionGoal(value))
                .HasColumnName("Goal")
                .IsRequired();

            entity.Property(s => s.Duration)
                .HasConversion(d => (long)d.Value.TotalMinutes, v => new SessionDuration(TimeSpan.FromMinutes(v)))
                .HasColumnName("DurationMinutes")
                .IsRequired();

            entity.Property(s => s.Status).HasConversion<string>().IsRequired();
            entity.Property(s => s.BlockProfileId).IsRequired();

            entity.OwnsOne(s => s.BreakSettings, b =>
            {
                b.Property(x => x.IntervalMinutes).HasColumnName("BreakIntervalMinutes");
                b.Property(x => x.BreakLengthMinutes).HasColumnName("BreakLengthMinutes");
            });
        });

        // ── BlockProfile ───────────────────────────────────────────────────
        modelBuilder.Entity<BlockProfile>(entity =>
        {
            entity.ToTable("BlockProfiles");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.Name).IsRequired().HasMaxLength(60);
            entity.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(p => p.WebsitePolicy).HasConversion<string>().IsRequired();
            entity.Property<string>("TagsSerialized").HasColumnName("Tags").HasDefaultValue("");

            entity.OwnsMany(p => p.BlockedApps, a =>
            {
                a.ToTable("BlockedApps");
                a.WithOwner().HasForeignKey("BlockProfileId");
                a.Property(x => x.ProcessName).IsRequired().HasMaxLength(200);
                a.HasKey("BlockProfileId", "ProcessName");
            });

            entity.OwnsMany(p => p.Websites, s =>
            {
                s.ToTable("BlockedSites");
                s.WithOwner().HasForeignKey("BlockProfileId");
                s.Property(x => x.Url).IsRequired().HasMaxLength(500);
                s.HasKey("BlockProfileId", "Url");
            });
        });

        // ── Planned blocks (TPH for FocusBlock / PassiveBlock) ──────────────
        modelBuilder.Entity<PlannedBlock>(entity =>
        {
            entity.ToTable("PlannedBlocks");
            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id).ValueGeneratedNever();
            entity.Property(b => b.CreatedAt).IsRequired();
            entity.Property(b => b.BlockProfileId).IsRequired();
            entity.Property(b => b.Type).HasConversion<string>().IsRequired();
            entity.Property(b => b.Start).IsRequired();
            entity.Property(b => b.End).IsRequired();
            entity.Property(b => b.IsPlanned).IsRequired().HasDefaultValue(false);

            entity.HasDiscriminator<string>("BlockKind")
                .HasValue<FocusBlock>("Focus")
                .HasValue<PassiveBlock>("Passive");
        });

        modelBuilder.Entity<FocusBlock>(entity =>
        {
            entity.Property(f => f.SessionId);
        });

        // ── BlockedAttempt ──────────────────────────────────────────────────
        modelBuilder.Entity<BlockedAttempt>(entity =>
        {
            entity.ToTable("BlockedAttempts");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.ResourceName).IsRequired().HasMaxLength(500);
            entity.Property(a => a.AttemptType).HasConversion<string>().IsRequired();
            entity.Property(a => a.Timestamp).IsRequired();

            // Indices for fast history lookup in Insights
            entity.HasIndex(a => a.SessionId);
            entity.HasIndex(a => a.Timestamp);
        });
    }
}