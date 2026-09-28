using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using System.Globalization;
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
    public DbSet<BreakTimerPreset> BreakTimerPresets { get; set; } = null!;

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
    // A contract day's window is stored as local wall-clock "yyyy-MM-ddTHH:mm:ss" strings —
    // never through the UTC DateTime converter above, since a contract's days are local by
    // definition and a window may end at 24:00 (the next day's midnight).
    private sealed record ContractDayJson(string Date, string WindowStart, string WindowEnd, double? Floor);
    private sealed record DailyFloorRuleJson(string Kind, double? Hours);
    private sealed record RedemptionEntryJson(Guid MissedBlockId, double MinutesCovered);

    private const string LocalMomentFormat = "yyyy-MM-ddTHH:mm:ss";

    internal static string SerializeContractCalendar(ContractCalendar calendar) => JsonSerializer.Serialize(
        calendar.Days.Select(d => new ContractDayJson(
            d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            d.WindowStart.ToString(LocalMomentFormat, CultureInfo.InvariantCulture),
            d.WindowEnd.ToString(LocalMomentFormat, CultureInfo.InvariantCulture),
            d.Floor?.Hours)).ToList(),
        (JsonSerializerOptions?)null);

    internal static ContractCalendar DeserializeContractCalendar(string json) => new(
        JsonSerializer.Deserialize<List<ContractDayJson>>(json, (JsonSerializerOptions?)null)!.Select(d => new ContractDay(
            DateOnly.ParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact(d.WindowStart, LocalMomentFormat, CultureInfo.InvariantCulture),
            DateTime.ParseExact(d.WindowEnd, LocalMomentFormat, CultureInfo.InvariantCulture),
            d.Floor is { } hours ? new DailyFloor(hours) : null)));

    private static string SerializeDailyFloorRule(DailyFloorRule rule) => JsonSerializer.Serialize(rule switch
    {
        DailyFloorRule.Fixed f => new DailyFloorRuleJson("Fixed", f.Floor.Hours),
        DailyFloorRule.FollowsPlan => new DailyFloorRuleJson("FollowsPlan", null),
        _ => new DailyFloorRuleJson("None", null)
    }, (JsonSerializerOptions?)null);

    private static DailyFloorRule DeserializeDailyFloorRule(string json)
    {
        var dto = JsonSerializer.Deserialize<DailyFloorRuleJson>(json, (JsonSerializerOptions?)null)!;
        return dto.Kind switch
        {
            "Fixed" => new DailyFloorRule.Fixed(new DailyFloor(dto.Hours!.Value)),
            "FollowsPlan" => new DailyFloorRule.FollowsPlan(),
            _ => new DailyFloorRule.None()
        };
    }

    private static string SerializeRedemptionEntries(IReadOnlyList<RedemptionEntry> entries) => JsonSerializer.Serialize(
        entries.Select(e => new RedemptionEntryJson(e.MissedBlockId, e.MinutesCovered)).ToList(),
        (JsonSerializerOptions?)null);

    private static List<RedemptionEntry> DeserializeRedemptionEntries(string json) =>
        JsonSerializer.Deserialize<List<RedemptionEntryJson>>(json, (JsonSerializerOptions?)null)!
            .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
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

            // The session's BreakSchedule (Gap 63): one row per break, offsets from
            // StartedAt. Owned — rows only exist as part of their session, and are
            // replaced wholesale when Complete trims the schedule.
            entity.Ignore(s => s.BreakSchedule);
            entity.Ignore(s => s.BreaksTaken);
            entity.OwnsMany<SessionBreak>("_breaks", b =>
            {
                b.ToTable("SessionBreaks");
                b.WithOwner().HasForeignKey("SessionId");
                b.Property<int>("Id");
                b.HasKey("Id");
                b.Property(x => x.Kind)
                    .HasConversion<string>()
                    .IsRequired();
                b.Property(x => x.Start).IsRequired();
                b.Property(x => x.End).IsRequired();
                b.Ignore(x => x.Length);
            });

            entity.Property(s => s.BreakPresetName)
                .HasMaxLength(100);

            entity.Property(s => s.KeepBlockingDuringBreaks)
                .IsRequired()
                .HasDefaultValue(false);

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
                .HasMaxLength(60)
                .UseCollation("NOCASE");

            entity.Property(p => p.WebsiteFilterType)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(p => p.PresetType)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(BlockPresetType.Focus);

            // Scoped by PresetType, not global — a Focus preset and a Background preset
            // are chosen from separate, type-filtered contexts and never compared against
            // each other, so sharing a name isn't a real collision. Mirrors the seed data
            // below, which already has "Social Media Blocked" once per type.
            entity.HasIndex(p => new { p.Name, p.PresetType }).IsUnique();

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

            entity.Property(b => b.RedemptionEntries)
                .HasConversion(
                    entries => SerializeRedemptionEntries(entries),
                    json => (IReadOnlyList<RedemptionEntry>)DeserializeRedemptionEntries(json))
                .HasColumnName("RedemptionEntries")
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
                .HasMaxLength(60)
                .UseCollation("NOCASE");

            // No PresetType-style discriminator here (unlike BlockPreset) — a Contract
            // Preset has no scheduling-context split, so a single global unique name is
            // the correct scope.
            entity.HasIndex(p => p.Name).IsUnique();

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

            entity.Property(p => p.RedemptionPlacementPolicy)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(p => p.KeepBlockingDuringBreaks)
                .IsRequired()
                .HasDefaultValue(false);
        });

        modelBuilder.Entity<BreakTimerPreset>(entity =>
        {
            entity.ToTable("BreakTimerPresets");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id)
                .ValueGeneratedNever();

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(60)
                .UseCollation("NOCASE");

            // No lock/PresetType-style scoping here — a BreakTimerPreset is snapshotted
            // by value into a Session at Start, so a single global unique name is the
            // correct scope, same reasoning as ContractPreset's.
            entity.HasIndex(p => p.Name).IsUnique();

            var classicId = new Guid("b0000002-0000-0000-0000-000000000001");
            var deepWorkId = new Guid("b0000002-0000-0000-0000-000000000002");
            var flowId = new Guid("b0000002-0000-0000-0000-000000000003");

            // Classic/Deep Work/Flow are ordinary seeded rows, not a protected special
            // case — freely editable/deletable by the user like any preset they create
            // themselves later.
            entity.HasData(
                new { Id = classicId, Name = "Classic" },
                new { Id = deepWorkId, Name = "Deep Work" },
                new { Id = flowId, Name = "Flow" }
            );

            entity.OwnsOne(p => p.BreakTimer, c =>
            {
                c.Property(x => x.WorkMinutes)
                    .HasColumnName("WorkMinutes")
                    .IsRequired();
                c.Property(x => x.ShortBreakMinutes)
                    .HasColumnName("ShortBreakMinutes")
                    .IsRequired();
                c.Property(x => x.CyclesBeforeLongBreak)
                    .HasColumnName("CyclesBeforeLongBreak")
                    .IsRequired();
                c.Property(x => x.LongBreakMinutes)
                    .HasColumnName("LongBreakMinutes")
                    .IsRequired();

                c.HasData(
                    new { BreakTimerPresetId = classicId, WorkMinutes = 25, ShortBreakMinutes = 5, CyclesBeforeLongBreak = 4, LongBreakMinutes = 15 },
                    new { BreakTimerPresetId = deepWorkId, WorkMinutes = 50, ShortBreakMinutes = 10, CyclesBeforeLongBreak = 3, LongBreakMinutes = 20 },
                    new { BreakTimerPresetId = flowId, WorkMinutes = 90, ShortBreakMinutes = 20, CyclesBeforeLongBreak = 2, LongBreakMinutes = 30 }
                );
            });
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

            entity.Property(c => c.RedemptionPlacementPolicy)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(c => c.ContractPresetId);

            entity.Property(c => c.KeepBlockingDuringBreaks)
                .IsRequired()
                .HasDefaultValue(false);

            // The contract's days (Gap 65) — a private field, the single source of truth for
            // windows and floors. Replaced wholesale (never mutated), so reference equality is
            // enough for change tracking.
            entity.Property<ContractCalendar>("_calendar")
                .HasConversion(
                    calendar => SerializeContractCalendar(calendar),
                    json => DeserializeContractCalendar(json))
                .HasColumnName("Days")
                .IsRequired();

            entity.Property(c => c.FloorRule)
                .HasConversion(
                    rule => SerializeDailyFloorRule(rule),
                    json => DeserializeDailyFloorRule(json))
                .HasColumnName("FloorRule")
                .IsRequired();

            // Single-value object — a direct double? conversion needs no JSON, unlike
            // the days, which carry a shape worth serializing.
            entity.Property(c => c.ContractFloor)
                .HasConversion(
                    floor => floor == null ? (double?)null : floor.Hours,
                    hours => hours == null ? null : new ContractFloor(hours.Value))
                .HasColumnName("ContractFloor");

            entity.Property(c => c.CommittedFocusHours)
                .IsRequired();

            entity.Property(c => c.Reflection)
                .HasMaxLength(2000);

            entity.Property(c => c.PostContractAcknowledgedAt);

            // ChangeWindow stays unmapped — parked for v1, no Application-layer consumer
            // exists yet (see ARCHITECT_GOVERNANCE.md).
            entity.Ignore(c => c.ChangeWindow);
        });
    }
}