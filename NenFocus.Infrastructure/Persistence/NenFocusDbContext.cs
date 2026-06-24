using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

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

        });

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

            entity.Property(p => p.ProfileType)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(BlockType.Focus);

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

            var focusAllowlistId  = new Guid("a0000001-0000-0000-0000-000000000001");
            var focusBlocklistId  = new Guid("a0000001-0000-0000-0000-000000000002");
            var bgBlocklistId     = new Guid("a0000001-0000-0000-0000-000000000003");
            var bgAllowlistId     = new Guid("a0000001-0000-0000-0000-000000000004");

            entity.HasData(
                new { Id = focusAllowlistId, Name = "Focus — Allowlist",     WebsitePolicy = WebsitePolicy.Allowlist, ProfileType = BlockType.Focus,       IsActive = true, TagsSerialized = "" },
                new { Id = focusBlocklistId, Name = "Focus — Blocklist",      WebsitePolicy = WebsitePolicy.Blocklist, ProfileType = BlockType.Focus,       IsActive = true, TagsSerialized = "" },
                new { Id = bgBlocklistId,    Name = "Background — Blocklist", WebsitePolicy = WebsitePolicy.Blocklist, ProfileType = BlockType.Background,  IsActive = true, TagsSerialized = "" },
                new { Id = bgAllowlistId,    Name = "Background — Allowlist", WebsitePolicy = WebsitePolicy.Allowlist, ProfileType = BlockType.Background,  IsActive = true, TagsSerialized = "" }
            );

            entity.OwnsMany(p => p.Websites, s =>
            {
                s.ToTable("BlockedSites");
                s.WithOwner().HasForeignKey("BlockProfileId");
                s.Property(x => x.Url)
                    .IsRequired()
                    .HasMaxLength(500);
                s.HasKey("BlockProfileId", "Url");
                s.HasData(
                    new { BlockProfileId = focusAllowlistId, Url = "gemini.google.com" },
                    new { BlockProfileId = focusAllowlistId, Url = "claude.ai" },
                    new { BlockProfileId = focusAllowlistId, Url = "github.com" },

                    new { BlockProfileId = focusBlocklistId, Url = "youtube.com" },
                    new { BlockProfileId = focusBlocklistId, Url = "twitter.com" },
                    new { BlockProfileId = focusBlocklistId, Url = "instagram.com" },
                    new { BlockProfileId = focusBlocklistId, Url = "reddit.com" },
                    new { BlockProfileId = focusBlocklistId, Url = "facebook.com" },

                    new { BlockProfileId = bgBlocklistId, Url = "youtube.com" },
                    new { BlockProfileId = bgBlocklistId, Url = "twitter.com" },
                    new { BlockProfileId = bgBlocklistId, Url = "instagram.com" },
                    new { BlockProfileId = bgBlocklistId, Url = "reddit.com" },
                    new { BlockProfileId = bgBlocklistId, Url = "facebook.com" },

                    new { BlockProfileId = bgAllowlistId, Url = "google.com" },
                    new { BlockProfileId = bgAllowlistId, Url = "soundcloud.com" },
                    new { BlockProfileId = bgAllowlistId, Url = "mail.google.com" },
                    new { BlockProfileId = bgAllowlistId, Url = "gemini.google.com" },
                    new { BlockProfileId = bgAllowlistId, Url = "claude.ai" },
                    new { BlockProfileId = bgAllowlistId, Url = "github.com" }
                );
            });
        });

        // TPH: FocusBlock and BackgroundBlock share this table via a BlockKind discriminator.
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
                .HasValue<FocusBlock>("Focus")
                .HasValue<BackgroundBlock>("Background");
        });

        modelBuilder.Entity<FocusBlock>(entity =>
        {
            entity.Property(r => r.RitualCompletedAt);
        });

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