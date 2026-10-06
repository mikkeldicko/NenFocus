using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        // Gap 88: a Moment is stored as the same UTC DateTime column as today — no migration.
        configurationBuilder
            .Properties<Moment>()
            .HaveConversion<MomentConverter>();
    }

    private class MomentConverter : ValueConverter<Moment, DateTime>
    {
        public MomentConverter()
            : base(
                v => v.ToStorage(),
                v => Moment.FromStorage(DateTime.SpecifyKind(v, DateTimeKind.Utc)))
        {
        }
    }

    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v : ThrowNonUtc(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }

        private static DateTime ThrowNonUtc() => throw new ArgumentException("Stored DateTimes must be UTC.");
    }

    private class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => !v.HasValue ? v : (v.Value.Kind == DateTimeKind.Utc ? v : ThrowNonUtc()),
                v => !v.HasValue ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
        {
        }

        private static DateTime? ThrowNonUtc() => throw new ArgumentException("Stored DateTimes must be UTC.");
    }

    // JSON round-tripping DTOs for the value objects below — DateOnly dictionary keys are
    // serialized as "yyyy-MM-dd" strings rather than relying on System.Text.Json's own
    // dictionary-key handling, for explicit, provider-independent round-tripping.
    // A contract day's window is stored as local wall-clock "yyyy-MM-ddTHH:mm:ss" strings —
    // never through the UTC DateTime converter above, since a contract's days are local by
    // definition and a window may end at 24:00 (the next day's midnight).
    private sealed record ContractDayJson(string Date, string WindowStart, string WindowEnd, double? Floor);
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

    private static string SerializeRedemptionEntries(IReadOnlyList<RedemptionEntry> entries) => JsonSerializer.Serialize(
        entries.Select(e => new RedemptionEntryJson(e.MissedBlockId, e.MinutesCovered)).ToList(),
        (JsonSerializerOptions?)null);

    private static List<RedemptionEntry> DeserializeRedemptionEntries(string json) =>
        JsonSerializer.Deserialize<List<RedemptionEntryJson>>(json, (JsonSerializerOptions?)null)!
            .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
            .ToList();

    /// <summary>
    /// A ContractRules is stored flat on its owner's row, under the column names the settings
    /// had before they were one value object (Gap 93) — the same for a preset and a contract.
    /// </summary>
    private static void MapContractRules<TOwner>(OwnedNavigationBuilder<TOwner, ContractRules> rules)
        where TOwner : class
    {
        rules.Property(r => r.Strictness)
            .HasConversion<string>()
            .HasColumnName("StrictnessRule")
            .IsRequired();

        rules.Property(r => r.FixedDailyFloor)
            .HasConversion(
                f => f == null ? (double?)null : f.Hours,
                v => v == null ? null : new DailyFloor(v.Value))
            .HasColumnName("DailyFloorHours");

        rules.Property(r => r.AllowCancellation)
            .HasColumnName("AllowCancellation")
            .IsRequired();

        rules.Property(r => r.KeepBlockingDuringBreaks)
            .HasColumnName("KeepBlockingDuringBreaks")
            .IsRequired()
            .HasDefaultValue(false);

        rules.Property(r => r.RedemptionPlacement)
            .HasConversion<string>()
            .HasColumnName("RedemptionPlacementPolicy")
            .IsRequired();

        rules.Property(r => r.BonusBlocksFollowStrictness)
            .HasColumnName("BonusBlocksFollowStrictness")
            .IsRequired()
            .HasDefaultValue(false);
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
            // each other, so sharing a name isn't a real collision.
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
                new { Id = focusBlocklistId, Name = "DBA block",              WebsiteFilterType = WebsiteFilterType.Blocklist, PresetType = BlockPresetType.Focus,      TagsSerialized = "" },
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

                    new { BlockPresetId = focusBlocklistId, Url = "dba.dk" },

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

            // Gap 88: the block's time is its TimeSlot, stored in the same Start/End columns as
            // before (no migration). A slot's fields are Domain-only, so they're mapped by name.
            entity.OwnsOne(b => b.Scheduled, slot =>
            {
                slot.Property<Moment>("Start").HasColumnName("Start").IsRequired();
                slot.Property<Moment>("End").HasColumnName("End").IsRequired();
            });
            entity.Navigation(b => b.Scheduled).IsRequired();

            entity.Property(b => b.Title)
                .HasMaxLength(100);

            // Gap 97: the block's membership in a contract — id and role together or not at all,
            // stored on the block's own row. The role is Domain-only, so it's mapped by name.
            // A row with an id but no role would load as no membership.
            entity.OwnsOne(b => b.Membership, membership =>
            {
                membership.Property(m => m.ContractId).HasColumnName("ContractId").IsRequired();
                membership.Property<ContractRole>("Role").HasColumnName("ContractRole").HasConversion<string>().IsRequired();
            });

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
            // Gap 95: the block's stamps are Domain-only — every reader asks the block — so they're
            // mapped by name, like a slot's Start/End.
            entity.Property<Moment?>("RitualCompletedAt");
            entity.Property<Moment?>("SessionEndedAt");
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

            var lockdownId = new Guid("c0000003-0000-0000-0000-000000000001");
            var dailyFlexId = new Guid("c0000003-0000-0000-0000-000000000002");
            var periodStudyId = new Guid("c0000003-0000-0000-0000-000000000003");

            entity.OwnsOne(p => p.WorkHours, w =>
            {
                w.Property(x => x.Start).HasColumnName("WorkHoursStart");
                w.Property(x => x.End).HasColumnName("WorkHoursEnd");

                w.HasData(
                    new { ContractPresetId = lockdownId, Start = new TimeOnly(9, 0), End = new TimeOnly(17, 0) },
                    new { ContractPresetId = dailyFlexId, Start = new TimeOnly(8, 0), End = new TimeOnly(20, 0) },
                    new { ContractPresetId = periodStudyId, Start = new TimeOnly(7, 0), End = new TimeOnly(23, 0) }
                );
            });

            // One ordinary seeded row per strictness rule — freely editable/deletable like
            // the seeded block and break timer presets. Only Lockdown sets its own daily
            // floor; the other two derive it from the scheduled blocks.
            entity.OwnsOne(p => p.Rules, rules =>
            {
                MapContractRules(rules);

                rules.HasData(
                    new
                    {
                        ContractPresetId = lockdownId, Strictness = StrictnessRule.Locked,
                        FixedDailyFloor = new DailyFloor(4), AllowCancellation = false, KeepBlockingDuringBreaks = true,
                        RedemptionPlacement = RedemptionPlacementPolicy.AnywhereInContract, BonusBlocksFollowStrictness = false
                    },
                    new
                    {
                        ContractPresetId = dailyFlexId, Strictness = StrictnessRule.DailyFlexibility,
                        AllowCancellation = true, KeepBlockingDuringBreaks = false,
                        RedemptionPlacement = RedemptionPlacementPolicy.AnywhereInContract, BonusBlocksFollowStrictness = false
                    },
                    new
                    {
                        ContractPresetId = periodStudyId, Strictness = StrictnessRule.PeriodTimeBudget,
                        AllowCancellation = true, KeepBlockingDuringBreaks = false,
                        RedemptionPlacement = RedemptionPlacementPolicy.AnywhereInContract, BonusBlocksFollowStrictness = false
                    }
                );
            });
            entity.Navigation(p => p.Rules).IsRequired();

            entity.HasData(
                new { Id = lockdownId, Name = "Lockdown" },
                new { Id = dailyFlexId, Name = "Daily Flex" },
                new { Id = periodStudyId, Name = "Period Study" }
            );
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

            entity.Property(c => c.Status)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(c => c.ContractPresetId);

            // The contract's own copy of its rules, on its own row (Gap 93).
            entity.OwnsOne(c => c.Rules, MapContractRules);
            entity.Navigation(c => c.Rules).IsRequired();

            // The contract's days (Gap 65) — a private field, the single source of truth for
            // windows and floors. Replaced wholesale (never mutated), so reference equality is
            // enough for change tracking.
            entity.Property<ContractCalendar>("_calendar")
                .HasConversion(
                    calendar => SerializeContractCalendar(calendar),
                    json => DeserializeContractCalendar(json))
                .HasColumnName("Days")
                .IsRequired();


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