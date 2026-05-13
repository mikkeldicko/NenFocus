using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using System.Text.Json;

namespace NenFocus.Infrastructure.Persistence;

public class NenFocusDbContext : DbContext
{
    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<BlockProfile> BlockProfiles { get; set; } = null!;
    public DbSet<PlannedBlock> PlannedBlocks { get; set; } = null!;
    public DbSet<BlockedAttempt> BlockedAttempts { get; set; } = null!;

    public NenFocusDbContext(DbContextOptions<NenFocusDbContext> options)
        : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Pass the TYPE of the converter, not an instance
        configurationBuilder
            .Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();

        configurationBuilder
            .Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>();
    }

    // 1. Define the Converter for non-nullable DateTime
    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    // 2. Define the Converter for nullable DateTime?
    private class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => !v.HasValue ? v : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
                v => !v.HasValue ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
        {
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<DomainEvent>();

        // ── Session ────────────────────────────────────────────────────────
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Id)
                .ValueGeneratedNever();

            entity.Property(s => s.Origin)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(s => s.OriginatingBlockId);

            entity.Property(s => s.StartedAt);
            entity.Property(s => s.EndedAt);
            entity.Property(s => s.BreaksTaken)
                .IsRequired()
                .HasDefaultValue(0);
            entity.Property(s => s.BreakStartedAt);
            entity.Property(s => s.Reflection)
                .HasMaxLength(2000);

            entity.Property(s => s.Goal)
                .HasConversion(
                    goal => goal.Value,
                    value => new SessionGoal(value))
                .HasColumnName("Goal")
                .IsRequired();

            entity.Property(s => s.Duration)
                .HasConversion(
                    d => (long)d.Value.TotalMinutes,
                    v => new SessionDuration(TimeSpan.FromMinutes(v)))
                .HasColumnName("DurationMinutes")
                .IsRequired();

            entity.Property(s => s.Status)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(s => s.BlockProfileId)
                .IsRequired();

            entity.OwnsOne(s => s.BreakSettings, b =>
            {
                b.Property(x => x.IntervalMinutes)
                    .HasColumnName("BreakIntervalMinutes");
                b.Property(x => x.BreakLengthMinutes)
                    .HasColumnName("BreakLengthMinutes");
            });

            entity.Property(s => s.TemporaryBlockedApps)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("TEXT")
                .HasDefaultValueSql("'[]'");

            entity.Property(s => s.TemporaryBlockedSites)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("TEXT")
                .HasDefaultValueSql("'[]'");
        });

        // ── BlockProfile ───────────────────────────────────────────────────
        modelBuilder.Entity<BlockProfile>(entity =>
        {
            entity.ToTable("BlockProfiles");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id)
                .ValueGeneratedNever();

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(60);

            entity.Property(p => p.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(p => p.WebsitePolicy)
                .HasConversion<string>()
                .IsRequired();

            entity.Property<string>("TagsSerialized")
                .HasColumnName("Tags")
                .HasDefaultValue("");

            entity.OwnsMany(p => p.BlockedApps, a =>
            {
                a.ToTable("BlockedApps");
                a.WithOwner().HasForeignKey("BlockProfileId");
                a.Property(x => x.ProcessName)
                    .IsRequired()
                    .HasMaxLength(200);
                a.HasKey("BlockProfileId", "ProcessName");
            });

            entity.OwnsMany(p => p.Websites, s =>
            {
                s.ToTable("BlockedSites");
                s.WithOwner().HasForeignKey("BlockProfileId");
                s.Property(x => x.Url)
                    .IsRequired()
                    .HasMaxLength(500);
                s.HasKey("BlockProfileId", "Url");
            });
        });

        // ── PlannedBlocks (TPH for RitualBlock / BackgroundBlock) ──────────
        modelBuilder.Entity<PlannedBlock>(entity =>
        {
            entity.ToTable("PlannedBlocks");
            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id)
                .ValueGeneratedNever();

            entity.Property(b => b.BlockProfileId)
                .IsRequired();

            entity.Property(b => b.Start)
                .IsRequired();

            entity.Property(b => b.End)
                .IsRequired();

            entity.Property(b => b.Title)
                .HasMaxLength(100);

            entity.HasDiscriminator<string>("BlockKind")
                .HasValue<RitualBlock>("Ritual")
                .HasValue<BackgroundBlock>("Background");
        });

        modelBuilder.Entity<RitualBlock>(entity =>
        {
            entity.Property(r => r.RitualCompletedAt);
        });

        // ── BlockedAttempt ─────────────────────────────────────────────────
        modelBuilder.Entity<BlockedAttempt>(entity =>
        {
            entity.ToTable("BlockedAttempts");
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Id)
                .ValueGeneratedNever();

            entity.Property(a => a.SessionId)
                .IsRequired();

            entity.Property(a => a.ResourceName)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(a => a.AttemptType)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(a => a.OccurredAt)
                .IsRequired();

            entity.HasIndex(a => a.SessionId);
            entity.HasIndex(a => a.OccurredAt);
        });
    }
}