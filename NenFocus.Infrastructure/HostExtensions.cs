using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Infrastructure;

public static class HostExtensions
{
    /// <summary>
    /// Ensures the SQLite database exists and all migrations are applied.
    /// Call this once at app startup before the UI launches.
    /// </summary>
    public static IHost EnsureDatabase(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NenFocusDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NenFocusDbContext>>();

        // Clear any stale migration lock left by a previously killed process.
        try { context.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsLock"); }
        catch (SqliteException ex) { logger.LogDebug(ex, "Could not clear migration lock — table may not exist yet on first run"); }

        // The RemoveTemporaryBlockedColumns migration (20260623123009) had an empty Up(),
        // so it never actually dropped those columns. If they already exist, mark the
        // AddTemporaryBlockedColumnsToSession migration as applied so EF skips adding them.
        SkipAddColumnsIfAlreadyExist(context, logger);

        context.Database.Migrate();

#if DEBUG
        SeedDebugTestContracts(context, logger);
#endif

        return host;
    }

#if DEBUG
    /// <summary>
    /// Debug-only test fixtures for exercising Gap L (contract completion grading,
    /// Post-Contract recap, Work-Hours-aware boundary) end to end — never compiled into a
    /// Release build. Runs once per fresh database (skipped whenever any NenContract
    /// already exists); every date is computed relative to real "today" at seed time.
    ///
    /// Replaces the previous redemption-placement-policy fixture set entirely (2026-09-07,
    /// at the architect's request) — that feature already shipped; this seed exists to
    /// exercise whatever is currently being built, not to remain a permanent regression
    /// fixture for every past feature.
    ///
    /// Contract date ranges stay sequential/non-overlapping — a system invariant relied on
    /// elsewhere (see ARCHITECT_GOVERNANCE.md, Open Gap G's Bonus Block resolution) — so
    /// only one "currently active" scenario is reachable without jumping the debug clock
    /// (Ctrl+Shift+T) forward into the next one's window, mirroring the previous fixture
    /// set's own testing workflow.
    /// </summary>
    private static void SeedDebugTestContracts(NenFocusDbContext context, ILogger logger)
    {
        try
        {
            if (context.NenContracts.Any()) return;

            // Same Guid as the "Social Media Blocked" Focus BlockPreset seeded above in
            // OnModelCreating — reused rather than duplicated.
            var focusPresetId = new Guid("a0000001-0000-0000-0000-000000000002");
            var today = DateTime.UtcNow.Date;

            // A completed FocusBlock + its matching completed Session — RitualCompletedAt
            // pinned to the block's own Start and Session.EndedAt pinned to its own End, so
            // IFocusBlockOutcomeClassifier reads it as cleanly CompletedOnTime with the full
            // scheduled duration credited. contractId null makes it a Bonus block instead of
            // a committed one.
            void SeedCompletedFocusBlock(Guid? contractId, DateTime start, double hours)
            {
                var end = start.AddHours(hours);
                var block = FocusBlock.Create(Guid.NewGuid(), focusPresetId, start, end, contractId: contractId);
                block.MarkRitualCompleted(start);
                context.PlannedBlocks.Add(block);

                var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focused work"),
                    new SessionDuration(end - start), focusPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
                session.Start(start, scheduledEnd: end);
                session.Complete(end);
                context.Sessions.Add(session);
            }

            // Never touched at all — no RitualCompletedAt, no Session. Classify resolves this
            // as Missed once "now" is past scheduledEnd, which it already is for every date
            // this is used with below (all safely in the past).
            void SeedMissedFocusBlock(Guid contractId, DateTime start, double hours)
                => context.PlannedBlocks.Add(FocusBlock.Create(Guid.NewGuid(), focusPresetId, start, start.AddHours(hours), contractId: contractId));

            // Ritual completed on time, but the session was ended before the block's own
            // scheduled End — CreditedWindow credits only what was actually delivered.
            void SeedEndedEarlyFocusBlock(Guid contractId, DateTime start, double scheduledHours, double actualHoursBeforeEndingEarly)
            {
                var end = start.AddHours(scheduledHours);
                var block = FocusBlock.Create(Guid.NewGuid(), focusPresetId, start, end, contractId: contractId);
                block.MarkRitualCompleted(start);
                context.PlannedBlocks.Add(block);

                var actualEnd = start.AddHours(actualHoursBeforeEndingEarly);
                var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focused work"),
                    new SessionDuration(end - start), focusPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
                session.Start(start, scheduledEnd: end);
                session.Complete(actualEnd);
                context.Sessions.Add(session);
            }

            // Deliberately left uncompleted — no RitualCompletedAt, no Session. The user
            // completes this live via the Ritual once its window arrives (may need a debug
            // clock jump to reach it), exercising the real end-to-end flow rather than
            // pre-faked history for a day that hasn't happened yet.
            void SeedPendingFocusBlock(Guid contractId, DateTime start, double hours)
                => context.PlannedBlocks.Add(FocusBlock.Create(Guid.NewGuid(), focusPresetId, start, start.AddHours(hours), contractId: contractId));

            // Reused across every "trivial plan" contract below (3, 5, 6) — same preset,
            // different contracts, exactly what a real reusable ContractPreset is for. Each
            // contract still gets its own distinct Name (a NenContract's Name is independent
            // of its preset's) — sharing "Focus Commitment" as the contract Name itself,
            // not just the preset, was confusing to tell apart in the Planner banner/popover.
            var trivialPreset = ContractPreset.Create(Guid.NewGuid(), "Focus Commitment", StrictnessRule.DailyFlexibility,
                dailyFloor: null, workHours: null, allowCancellation: true);
            context.ContractPresets.Add(trivialPreset);

            // Contract 1 — "In Progress", active from first launch, no clock jump needed.
            // Two real 2h days already completed (today-3, today-2); two more (tomorrow,
            // day after) are planned but deliberately left uncompleted — "Complete" stays
            // hidden (RemainingHours > 0) until you actually complete those two live via the
            // Ritual once each day's window arrives (jump the debug clock forward to reach
            // them without waiting for real time to pass). Once all four are done, the
            // recap has a real day-strip, a real streak, and real CompletedOnTime blocks —
            // genuinely earned, not pre-faked.
            var inProgressStart = DateOnly.FromDateTime(today.AddDays(-3));
            var inProgressPlan = new Dictionary<DateOnly, double>
            {
                [DateOnly.FromDateTime(today.AddDays(-3))] = 2,
                [DateOnly.FromDateTime(today.AddDays(-2))] = 2,
                [DateOnly.FromDateTime(today.AddDays(1))] = 2,
                [DateOnly.FromDateTime(today.AddDays(2))] = 2
            };
            var inProgressContract = NenContract.Seal(
                id: Guid.NewGuid(), name: "In Progress", goal: new NenContractGoal("Exercise the Post-Contract recap feature"),
                startDate: inProgressStart, endDate: DateOnly.FromDateTime(today.AddDays(3)), strictnessRule: trivialPreset.StrictnessRule,
                dailyFloorPolicy: DailyFloorPolicy.PerDay(inProgressPlan), changeWindow: null,
                allowCancellation: trivialPreset.AllowCancellation, plannedFocusHoursByDay: inProgressPlan,
                sealedAt: today, contractPresetId: trivialPreset.Id);
            context.NenContracts.Add(inProgressContract);
            SeedCompletedFocusBlock(inProgressContract.Id, today.AddDays(-3).AddHours(9), 2);
            SeedCompletedFocusBlock(inProgressContract.Id, today.AddDays(-2).AddHours(9), 2);
            SeedPendingFocusBlock(inProgressContract.Id, today.AddDays(1).AddHours(9), 2);
            SeedPendingFocusBlock(inProgressContract.Id, today.AddDays(2).AddHours(9), 2);

            // Contract 2 — starts 35 days out. Jump the debug clock to at least +35 days.
            // A real 4h/day floor, backed by real (uncompleted) blocks matching it exactly
            // — GetNenContractsForDateRangeQueryHandler's plannedForDay sums *actual*
            // scheduled blocks, not this Seal-time plannedFocusHoursByDay map, so without a
            // real block behind each day the calendar would show 0h/no tile at all despite
            // CommittedFocusHours correctly reporting a real total (a real bug this exact
            // gap surfaced in an earlier version of this seed — DayFloorPreviewViewModel.
            // ShowSimplifiedFuture assumes a future day's real scheduled hours can never
            // fall below its floor, which Seal's own validation guarantees for a normal
            // real contract but not for a plannedFocusHoursByDay number seeded with no
            // block to back it). "Complete" stays hidden (RemainingHours > 0) until these
            // are completed live via the Ritual once each day's window arrives.
            var hoursStart = DateOnly.FromDateTime(today.AddDays(35));
            var hoursEnd = DateOnly.FromDateTime(today.AddDays(41));
            var hoursPlan = new Dictionary<DateOnly, double>();
            for (var d = hoursStart; d <= hoursEnd; d = d.AddDays(1)) hoursPlan[d] = 4;
            var hoursPreset = ContractPreset.Create(Guid.NewGuid(), "Hours Remaining", StrictnessRule.DailyFlexibility,
                dailyFloor: new DailyFloor(4), workHours: null, allowCancellation: true);
            context.ContractPresets.Add(hoursPreset);
            var hoursContract = NenContract.Seal(
                id: Guid.NewGuid(), name: hoursPreset.Name, goal: new NenContractGoal("Exercise the Post-Contract recap feature"),
                startDate: hoursStart, endDate: hoursEnd, strictnessRule: hoursPreset.StrictnessRule,
                dailyFloorPolicy: DailyFloorPolicy.Fixed(new DailyFloor(4), restDays: []), changeWindow: null,
                allowCancellation: true, plannedFocusHoursByDay: hoursPlan, sealedAt: today, contractPresetId: hoursPreset.Id);
            context.NenContracts.Add(hoursContract);
            foreach (var (date, hours) in hoursPlan)
                SeedPendingFocusBlock(hoursContract.Id, date.ToDateTime(new TimeOnly(9, 0)), hours);

            // Contract 3 — starts 46 days out. Jump to at least +46 days. Nothing committed
            // (CommittedFocusHours == 0, hours check trivially passes) and one still-open
            // linked block sits in the window — isolates the PendingBlockCount gate cleanly,
            // with nothing pre-completed (it hasn't happened yet relative to real "now",
            // so there's nothing honest to mark complete ahead of time). Its recap is never
            // seen anyway while the button stays hidden.
            var pendingStart = DateOnly.FromDateTime(today.AddDays(46));
            var pendingContract = NenContract.Seal(
                id: Guid.NewGuid(), name: "Pending Block Test", goal: new NenContractGoal("Exercise the Post-Contract recap feature"),
                startDate: pendingStart, endDate: DateOnly.FromDateTime(today.AddDays(52)), strictnessRule: trivialPreset.StrictnessRule,
                dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: trivialPreset.AllowCancellation,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(), sealedAt: today, contractPresetId: trivialPreset.Id);
            context.NenContracts.Add(pendingContract);
            var pendingBlockStart = pendingStart.ToDateTime(new TimeOnly(9, 0));
            context.PlannedBlocks.Add(FocusBlock.Create(
                Guid.NewGuid(), focusPresetId, pendingBlockStart, pendingBlockStart.AddHours(1), contractId: pendingContract.Id));

            // Contract 4 — starts 57 days out. Jump to at least +57 days. Work Hours set
            // (9-17); nothing committed (CommittedFocusHours == 0), so it's immediately
            // ready to complete once reached — exercises StartsAt/EndsAt at Seal/Complete
            // time without pretending anything already happened before real "now" got there.
            var workHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
            var workHoursPreset = ContractPreset.Create(Guid.NewGuid(), "Work Hours Boundary", StrictnessRule.DailyFlexibility,
                dailyFloor: null, workHours: workHours, allowCancellation: true);
            context.ContractPresets.Add(workHoursPreset);
            var workHoursStart = DateOnly.FromDateTime(today.AddDays(57));
            context.NenContracts.Add(NenContract.Seal(
                id: Guid.NewGuid(), name: workHoursPreset.Name, goal: new NenContractGoal("Exercise the Post-Contract recap feature"),
                startDate: workHoursStart, endDate: DateOnly.FromDateTime(today.AddDays(64)), strictnessRule: workHoursPreset.StrictnessRule,
                dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
                plannedFocusHoursByDay: new Dictionary<DateOnly, double>(), sealedAt: today,
                workHoursPolicy: new WorkHoursPolicy(workHours), contractPresetId: workHoursPreset.Id));

            // Contract 5 — "Completed Last Month," already Completed, never acknowledged.
            // A real mix of outcomes, not just CompletedOnTime: 2 real completed days, 1
            // Missed (never touched), 1 EndedEarly (ritual done on time, session cut short
            // at 1h of a scheduled 2h) — 5h delivered out of 8h committed, so this also
            // exercises the "Partially Completed" grade band, not just 100%+. Dates are
            // safely in the past and irrelevant to its terminal status (forced Complete()
            // directly). Tests the startup catch-up sweep: launching the app should show
            // this Post-Contract overlay automatically, no clicking required.
            var completedStart = DateOnly.FromDateTime(today.AddDays(-60));
            var completedPlan = new Dictionary<DateOnly, double>
            {
                [completedStart] = 2,
                [completedStart.AddDays(1)] = 2,
                [completedStart.AddDays(2)] = 2,
                [completedStart.AddDays(3)] = 2
            };
            var completedContract = NenContract.Seal(
                id: Guid.NewGuid(), name: "Completed Last Month", goal: new NenContractGoal("Exercise the Post-Contract recap feature"),
                startDate: completedStart, endDate: DateOnly.FromDateTime(today.AddDays(-53)), strictnessRule: trivialPreset.StrictnessRule,
                dailyFloorPolicy: DailyFloorPolicy.PerDay(completedPlan), changeWindow: null,
                allowCancellation: trivialPreset.AllowCancellation, plannedFocusHoursByDay: completedPlan,
                sealedAt: today, contractPresetId: trivialPreset.Id);
            context.NenContracts.Add(completedContract);
            SeedCompletedFocusBlock(completedContract.Id, completedStart.ToDateTime(new TimeOnly(9, 0)), 2);
            SeedCompletedFocusBlock(completedContract.Id, completedStart.AddDays(1).ToDateTime(new TimeOnly(9, 0)), 2);
            SeedMissedFocusBlock(completedContract.Id, completedStart.AddDays(2).ToDateTime(new TimeOnly(9, 0)), 2);
            SeedEndedEarlyFocusBlock(completedContract.Id, completedStart.AddDays(3).ToDateTime(new TimeOnly(9, 0)), scheduledHours: 2, actualHoursBeforeEndingEarly: 1);
            completedContract.Complete(completedStart.ToDateTime(TimeOnly.MinValue).AddDays(4));

            // Contract 6 — "Broken Contract Test," already Broken, never acknowledged. Two
            // real completed days and one Missed day before the break point, plus a fourth
            // block scheduled after it that gets MarkCancelled the same way
            // BreakNenContractCommandHandler would — giving the recap's Cancelled *and*
            // Missed counts real content in the same scenario, and a concrete illustration
            // of why EndDate shrinks to the break date while OriginalEndDate stays what was
            // actually sealed. Same startup-catchup path as Contract 5, but exercises the
            // "CONTRACT BROKEN" header/reflection prompt.
            var brokenStart = DateOnly.FromDateTime(today.AddDays(-75));
            var brokenPlan = new Dictionary<DateOnly, double>
            {
                [brokenStart] = 2,
                [brokenStart.AddDays(1)] = 2,
                [brokenStart.AddDays(2)] = 2
            };
            var brokenContract = NenContract.Seal(
                id: Guid.NewGuid(), name: "Broken Contract Test", goal: new NenContractGoal("Exercise the Post-Contract recap feature"),
                startDate: brokenStart, endDate: DateOnly.FromDateTime(today.AddDays(-68)), strictnessRule: trivialPreset.StrictnessRule,
                dailyFloorPolicy: DailyFloorPolicy.PerDay(brokenPlan), changeWindow: null,
                allowCancellation: trivialPreset.AllowCancellation, plannedFocusHoursByDay: brokenPlan,
                sealedAt: today, contractPresetId: trivialPreset.Id);
            context.NenContracts.Add(brokenContract);
            SeedCompletedFocusBlock(brokenContract.Id, brokenStart.ToDateTime(new TimeOnly(9, 0)), 2);
            SeedMissedFocusBlock(brokenContract.Id, brokenStart.AddDays(1).ToDateTime(new TimeOnly(9, 0)), 2);
            SeedCompletedFocusBlock(brokenContract.Id, brokenStart.AddDays(2).ToDateTime(new TimeOnly(9, 0)), 2);
            var cancelledBlockStart = brokenStart.AddDays(4).ToDateTime(new TimeOnly(9, 0));
            var cancelledBlock = FocusBlock.Create(Guid.NewGuid(), focusPresetId,
                cancelledBlockStart, cancelledBlockStart.AddHours(2), contractId: brokenContract.Id);
            context.PlannedBlocks.Add(cancelledBlock);
            var brokenAt = brokenStart.ToDateTime(TimeOnly.MinValue).AddDays(3).AddHours(12); // between day 3's completion and day 5's cancelled block
            brokenContract.Break(brokenAt);
            cancelledBlock.MarkCancelled(brokenAt);

            context.SaveChanges();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not seed debug test contracts");
        }
    }
#endif

    private static void SkipAddColumnsIfAlreadyExist(NenFocusDbContext context, ILogger logger)
    {
        const string migrationId = "20260624185307_AddTemporaryBlockedColumnsToSession";

        try
        {
            var conn = context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA table_info(Sessions)";
            using var reader = pragmaCmd.ExecuteReader();
            var cols = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
                cols.Add(reader.GetString(1));
            reader.Close();

            if (cols.Contains("TemporaryBlockedApps") && cols.Contains("TemporaryBlockedSites"))
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText =
                    $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                    $"VALUES ('{migrationId}', '9.0.2')";
                insertCmd.ExecuteNonQuery();
            }
        }
        catch (SqliteException ex)
        {
            logger.LogDebug(ex, "Could not inspect Sessions table — it may not exist yet on a brand-new database");
        }
    }
}