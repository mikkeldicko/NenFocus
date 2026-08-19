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
    public DbSet<BlockPreset> BlockPresets { get; set; } = null!;
    public DbSet<PlannedBlock> PlannedBlocks { get; set; } = null!;
    public DbSet<BlockedAttempt> BlockedAttempts { get; set; } = null!;
    public DbSet<ContractPreset> ContractPresets { get; set; } = null!;
    public DbSet<NenContract> NenContracts { get; set; } = null!;

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

    // JSON round-tripping DTOs for the value objects below — DateOnly dictionary keys are
    // serialized as "yyyy-MM-dd" strings rather than relying on System.Text.Json's own
    // dictionary-key handling, for explicit, provider-independent round-tripping.
    private sealed record WorkHoursJson(TimeOnly Start, TimeOnly End);
    private sealed record WorkHoursPolicyJson(WorkHoursJson? Default, Dictionary<string, WorkHoursJson?> PerDayExceptions);
    private sealed record DailyFloorPolicyJson(double? Default, Dictionary<string, double?> PerDayExceptions);
    private sealed record MakeUpForEntryJson(Guid MissedBlockId, double MinutesCovered);

    private static string SerializeDailyFloorPolicy(DailyFloorPolicy policy) => JsonSerializer.Serialize(
        new DailyFloorPolicyJson(
            policy.Default?.Hours,
            policy.PerDayExceptions.ToDictionary(kv => kv.Key.ToString("yyyy-MM-dd"), kv => kv.Value?.Hours)),
        (JsonSerializerOptions?)null);

    private static DailyFloorPolicy DeserializeDailyFloorPolicy(string json)
    {
        var dto = JsonSerializer.Deserialize<DailyFloorPolicyJson>(json, (JsonSerializerOptions?)null)!;
        return new DailyFloorPolicy(
            dto.Default is { } hours ? new DailyFloor(hours) : null,
            dto.PerDayExceptions.ToDictionary(kv => DateOnly.Parse(kv.Key), kv => kv.Value is { } v ? (DailyFloor?)new DailyFloor(v) : null));
    }

    private static string SerializeWorkHoursPolicy(WorkHoursPolicy policy) => JsonSerializer.Serialize(
        new WorkHoursPolicyJson(
            policy.Default is { } d ? new WorkHoursJson(d.Start, d.End) : null,
            policy.PerDayExceptions.ToDictionary(kv => kv.Key.ToString("yyyy-MM-dd"), kv => kv.Value is { } w ? new WorkHoursJson(w.Start, w.End) : null)),
        (JsonSerializerOptions?)null);

    private static WorkHoursPolicy DeserializeWorkHoursPolicy(string json)
    {
        var dto = JsonSerializer.Deserialize<WorkHoursPolicyJson>(json, (JsonSerializerOptions?)null)!;
        return new WorkHoursPolicy(
            dto.Default is { } d ? new WorkHours(d.Start, d.End) : null,
            dto.PerDayExceptions.ToDictionary(kv => DateOnly.Parse(kv.Key), kv => kv.Value is { } w ? (WorkHours?)new WorkHours(w.Start, w.End) : null));
    }

    private static string SerializeMakeUpForEntries(IReadOnlyList<MakeUpForEntry> entries) => JsonSerializer.Serialize(
        entries.Select(e => new MakeUpForEntryJson(e.MissedBlockId, e.MinutesCovered)).ToList(),
        (JsonSerializerOptions?)null);

    private static List<MakeUpForEntry> DeserializeMakeUpForEntries(string json) =>
        JsonSerializer.Deserialize<List<MakeUpForEntryJson>>(json, (JsonSerializerOptions?)null)!
            .Select(e => new MakeUpForEntry(e.MissedBlockId, e.MinutesCovered))
            .ToList();

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
            entity.Property(s => s.EndAt);
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

            entity.Property(s => s.BlockPresetId)
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
                    list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnName("TemporaryBlockedApps")
                .HasDefaultValueSql("'[]'");

            entity.Property(s => s.TemporaryBlockedSites)
                .HasConversion(
                    list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnName("TemporaryBlockedSites")
                .HasDefaultValueSql("'[]'");

        });

        modelBuilder.Entity<BlockPreset>(entity =>
        {
            entity.ToTable("BlockPresets");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id)
                .ValueGeneratedNever();

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(60);

            entity.Property(p => p.WebsiteFilterType)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(p => p.PresetType)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(BlockPresetType.Focus);

            entity.Property<string>("TagsSerialized")
                .HasColumnName("Tags")
                .HasDefaultValue("");

            entity.OwnsMany(p => p.BlockedApps, a =>
            {
                a.ToTable("BlockedApps");
                a.WithOwner().HasForeignKey("BlockPresetId");
                a.Property(x => x.ProcessName)
                    .IsRequired()
                    .HasMaxLength(200);
                a.HasKey("BlockPresetId", "ProcessName");
            });

            var focusAllowlistId  = new Guid("a0000001-0000-0000-0000-000000000001");
            var focusBlocklistId  = new Guid("a0000001-0000-0000-0000-000000000002");
            var bgBlocklistId     = new Guid("a0000001-0000-0000-0000-000000000003");
            var bgAllowlistId     = new Guid("a0000001-0000-0000-0000-000000000004");

            entity.HasData(
                new { Id = focusAllowlistId, Name = "AI & Dev Tools Only",    WebsiteFilterType = WebsiteFilterType.Allowlist, PresetType = BlockPresetType.Focus,      TagsSerialized = "" },
                new { Id = focusBlocklistId, Name = "Social Media Blocked",   WebsiteFilterType = WebsiteFilterType.Blocklist, PresetType = BlockPresetType.Focus,      TagsSerialized = "" },
                new { Id = bgBlocklistId,    Name = "Social Media Blocked",   WebsiteFilterType = WebsiteFilterType.Blocklist, PresetType = BlockPresetType.Background, TagsSerialized = "" },
                new { Id = bgAllowlistId,    Name = "Work & Music Only",      WebsiteFilterType = WebsiteFilterType.Allowlist, PresetType = BlockPresetType.Background, TagsSerialized = "" }
            );

            entity.OwnsMany(p => p.Websites, s =>
            {
                s.ToTable("BlockedSites");
                s.WithOwner().HasForeignKey("BlockPresetId");
                s.Property(x => x.Url)
                    .IsRequired()
                    .HasMaxLength(500);
                s.HasKey("BlockPresetId", "Url");
                s.HasData(
                    new { BlockPresetId = focusAllowlistId, Url = "gemini.google.com" },
                    new { BlockPresetId = focusAllowlistId, Url = "claude.ai" },
                    new { BlockPresetId = focusAllowlistId, Url = "github.com" },

                    new { BlockPresetId = focusBlocklistId, Url = "youtube.com" },
                    new { BlockPresetId = focusBlocklistId, Url = "twitter.com" },
                    new { BlockPresetId = focusBlocklistId, Url = "instagram.com" },
                    new { BlockPresetId = focusBlocklistId, Url = "reddit.com" },
                    new { BlockPresetId = focusBlocklistId, Url = "facebook.com" },

                    new { BlockPresetId = bgBlocklistId, Url = "youtube.com" },
                    new { BlockPresetId = bgBlocklistId, Url = "twitter.com" },
                    new { BlockPresetId = bgBlocklistId, Url = "instagram.com" },
                    new { BlockPresetId = bgBlocklistId, Url = "reddit.com" },
                    new { BlockPresetId = bgBlocklistId, Url = "facebook.com" },

                    new { BlockPresetId = bgAllowlistId, Url = "google.com" },
                    new { BlockPresetId = bgAllowlistId, Url = "soundcloud.com" },
                    new { BlockPresetId = bgAllowlistId, Url = "mail.google.com" },
                    new { BlockPresetId = bgAllowlistId, Url = "gemini.google.com" },
                    new { BlockPresetId = bgAllowlistId, Url = "claude.ai" },
                    new { BlockPresetId = bgAllowlistId, Url = "github.com" }
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

            entity.Property(b => b.BlockPresetId)
                .IsRequired();

            entity.Property(b => b.Start)
                .IsRequired();

            entity.Property(b => b.End)
                .IsRequired();

            entity.Property(b => b.Title)
                .HasMaxLength(100);

            entity.Property(b => b.ContractId);

            entity.Property(b => b.CancelledAt);

            entity.Property(b => b.MakeUpForEntries)
                .HasConversion(
                    entries => SerializeMakeUpForEntries(entries),
                    json => (IReadOnlyList<MakeUpForEntry>)DeserializeMakeUpForEntries(json))
                .HasColumnName("MakeUpForEntries")
                .HasDefaultValueSql("'[]'");

            entity.HasDiscriminator<string>("BlockKind")
                .HasValue<FocusBlock>("Focus")
                .HasValue<BackgroundBlock>("Background");
        });

        modelBuilder.Entity<FocusBlock>(entity =>
        {
            entity.Property(r => r.RitualCompletedAt);
            entity.Property(r => r.SessionEndedAt);
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

        modelBuilder.Entity<ContractPreset>(entity =>
        {
            entity.ToTable("ContractPresets");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id)
                .ValueGeneratedNever();

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(60);

            entity.Property(p => p.StrictnessRule)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(p => p.DailyFloor)
                .HasConversion(
                    f => f == null ? (double?)null : f.Hours,
                    v => v == null ? null : new DailyFloor(v.Value))
                .HasColumnName("DailyFloorHours");

            entity.OwnsOne(p => p.WorkHours, w =>
            {
                w.Property(x => x.Start).HasColumnName("WorkHoursStart");
                w.Property(x => x.End).HasColumnName("WorkHoursEnd");
            });

            entity.Property(p => p.AllowCancellation)
                .IsRequired();
        });

        modelBuilder.Entity<NenContract>(entity =>
        {
            entity.ToTable("NenContracts");
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Id)
                .ValueGeneratedNever();

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.Goal)
                .HasConversion(
                    goal => goal.Value,
                    value => new NenContractGoal(value))
                .HasColumnName("Goal")
                .IsRequired();

            entity.Property(c => c.StartDate)
                .IsRequired();

            entity.Property(c => c.EndDate)
                .IsRequired();

            entity.Property(c => c.StrictnessRule)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(c => c.Status)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(c => c.AllowCancellation)
                .IsRequired();

            entity.Property(c => c.ContractPresetId);

            entity.Property(c => c.DailyFloorPolicy)
                .HasConversion(
                    policy => SerializeDailyFloorPolicy(policy),
                    json => DeserializeDailyFloorPolicy(json))
                .HasColumnName("DailyFloorPolicy")
                .IsRequired();

            entity.Property(c => c.WorkHoursPolicy)
                .HasConversion(
                    policy => policy == null ? null : SerializeWorkHoursPolicy(policy),
                    json => json == null ? null : DeserializeWorkHoursPolicy(json))
                .HasColumnName("WorkHoursPolicy");

            // Single-value object — a direct double? conversion needs no JSON, unlike
            // DailyFloorPolicy/WorkHoursPolicy which carry a shape worth serializing.
            entity.Property(c => c.ContractFloor)
                .HasConversion(
                    floor => floor == null ? (double?)null : floor.Hours,
                    hours => hours == null ? null : new ContractFloor(hours.Value))
                .HasColumnName("ContractFloor");

            // ChangeWindow stays unmapped — parked for v1, no Application-layer consumer
            // exists yet (see ARCHITECT_GOVERNANCE.md), unlike WorkHoursPolicy/DailyFloorPolicy
            // which already have real guards built against them even without wizard UI.
            entity.Ignore(c => c.ChangeWindow);
        });
    }
}