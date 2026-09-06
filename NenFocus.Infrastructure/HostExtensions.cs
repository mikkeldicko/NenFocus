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
    /// Debug-only test fixtures for exercising the redemption-placement-policy feature end
    /// to end — never compiled into a Release build. Runs once per fresh database (skipped
    /// whenever any NenContract already exists), and every date is computed relative to real
    /// "today" at seed time rather than baked into the migration, so wiping the DB and
    /// relaunching always produces a genuinely testable deficit with no manual setup.
    ///
    /// Each contract is backed by a real, saved ContractPreset (ContractPresetId set, visible
    /// on the Presets page) — matching actual user experience, since Advanced contracts can
    /// only ever be sealed from a saved preset (Gap 27), never freeform rules. Contract date
    /// ranges are sequential, never overlapping — NenContract ranges are a system invariant
    /// relied on elsewhere (see ARCHITECT_GOVERNANCE.md, Open Gap G's Bonus Block resolution)
    /// and only one contract can ever be "the" active one the Planner banner/Cover Deficit
    /// button operate against at a time. Contract 1 is active immediately; reaching the rest
    /// means jumping the debug clock forward (Ctrl+Shift+T) into their windows — each one's
    /// own first-day block only becomes a real deficit once the clock has advanced at least
    /// one day past it.
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

            ContractPreset SeedPreset(string name, StrictnessRule strictness, RedemptionPlacementPolicy policy, DailyFloor? dailyFloor = null)
            {
                var preset = ContractPreset.Create(Guid.NewGuid(), name, strictness, dailyFloor, workHours: null,
                    allowCancellation: true, redemptionPlacementPolicy: policy);
                context.ContractPresets.Add(preset);
                return preset;
            }

            void SeedContract(ContractPreset preset, DateOnly startDate, DateOnly endDate,
                IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay, ContractFloor? contractFloor = null)
            {
                // Mirrors exactly how a real Advanced contract's DailyFloorPolicy gets built
                // from a picked preset (SealNenContractCommandHandler): a preset with a real
                // DailyFloor becomes a fixed policy with no rest days; one with none (Period
                // Budget's own no-per-day-floor mode, or these mostly-None test presets)
                // stays DailyFloorPolicy.None.
                var dailyFloorPolicy = preset.DailyFloor is { } floor
                    ? DailyFloorPolicy.Fixed(floor, restDays: [])
                    : DailyFloorPolicy.None;

                var contractId = Guid.NewGuid();
                var contract = NenContract.Seal(
                    id: contractId,
                    name: preset.Name,
                    goal: new NenContractGoal("Exercise the redemption placement policy fix"),
                    startDate: startDate,
                    endDate: endDate,
                    strictnessRule: preset.StrictnessRule,
                    dailyFloorPolicy: dailyFloorPolicy,
                    changeWindow: null,
                    allowCancellation: preset.AllowCancellation,
                    plannedFocusHoursByDay: plannedFocusHoursByDay,
                    sealedAt: today,
                    contractPresetId: preset.Id,
                    redemptionPlacementPolicy: preset.RedemptionPlacementPolicy,
                    contractFloor: contractFloor);

                context.NenContracts.Add(contract);

                foreach (var (date, hours) in plannedFocusHoursByDay)
                {
                    var blockStart = date.ToDateTime(new TimeOnly(9, 0));
                    context.PlannedBlocks.Add(FocusBlock.Create(
                        Guid.NewGuid(), focusPresetId, blockStart, blockStart.AddHours(hours), contractId: contractId));
                }
            }

            // Contract 1 — active from first launch, no clock jump needed. Directly
            // reproduces the originally-reported bug: a block missed yesterday must still
            // be coverable today.
            SeedContract(
                SeedPreset("Current Day Only", StrictnessRule.DailyFlexibility, RedemptionPlacementPolicy.CurrentDayOnly),
                DateOnly.FromDateTime(today.AddDays(-3)), DateOnly.FromDateTime(today.AddDays(7)),
                new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(today.AddDays(-1))] = 1.0 });

            // Contract 2 — starts 8 days out. Jump the debug clock to at least +9 days to
            // make its own Day-1 block count as "missed."
            SeedContract(
                SeedPreset("Anywhere In Contract", StrictnessRule.DailyFlexibility, RedemptionPlacementPolicy.AnywhereInContract),
                DateOnly.FromDateTime(today.AddDays(8)), DateOnly.FromDateTime(today.AddDays(18)),
                new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(today.AddDays(8))] = 1.0 });

            // Contract 3 — starts 19 days out. Jump to at least +20 days. Period strictness
            // is what makes the "move a redemption block to another day" test meaningful.
            SeedContract(
                SeedPreset("Period + Move", StrictnessRule.PeriodTimeBudget, RedemptionPlacementPolicy.CurrentDayOnly),
                DateOnly.FromDateTime(today.AddDays(19)), DateOnly.FromDateTime(today.AddDays(29)),
                new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(today.AddDays(19))] = 1.0 },
                contractFloor: new ContractFloor(5));

            // Contract 4 — starts 30 days out. Jump to at least +31 days. Confirms a
            // redemption block gets no special exemption from Lockdown's immovability.
            SeedContract(
                SeedPreset("Lockdown", StrictnessRule.Locked, RedemptionPlacementPolicy.AnywhereInContract),
                DateOnly.FromDateTime(today.AddDays(30)), DateOnly.FromDateTime(today.AddDays(40)),
                new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(today.AddDays(30))] = 1.0 });

            // Contract 5 — starts 41 days out, only 3 days long. Jump to at least +41 days.
            // A REAL fixed 1h/day floor (unlike 1-4, which all use DailyFloorPolicy.None) —
            // every day is pre-planned with exactly 1h so Seal's own floor check passes.
            // Resize/extend one day's block past 1h and complete it to confirm the floor
            // badge stays pinned at "Xh / 1h" (surplus shown) instead of becoming "Xh / Xh."
            var floorContractStart = DateOnly.FromDateTime(today.AddDays(41));
            var floorContractEnd = DateOnly.FromDateTime(today.AddDays(43));
            var floorContractPlan = new Dictionary<DateOnly, double>();
            for (var date = floorContractStart; date <= floorContractEnd; date = date.AddDays(1))
                floorContractPlan[date] = 1.0;
            SeedContract(
                SeedPreset("Daily Floor Surplus", StrictnessRule.DailyFlexibility,
                    RedemptionPlacementPolicy.AnywhereInContract, dailyFloor: new DailyFloor(1)),
                floorContractStart, floorContractEnd, floorContractPlan);

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