using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence;
using TestAt = NenFocus.Tests.At;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 93: a contract's rules are one value object, stored flat on the preset's and the contract's
/// own row under the column names the settings had before. Read back through a second context,
/// so each check is a real round trip.
/// </summary>
public class ContractRulesPersistenceTests : SqliteRepositoryTestBase
{
    private static readonly DateTime Day = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ContractRules EverySettingOffDefault = new(
        StrictnessRule.DailyFlexibility, new DailyFloor(3), allowCancellation: true, keepBlockingDuringBreaks: true,
        RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true);

    private static NenContract Sealed(ContractRules rules) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        rules: rules,
        calendar: TestCalendars.For(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day).AddDays(2), new DailyFloorRule.None()),
        changeWindow: null,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: TestAt.Utc(Day),
        timeZone: TestAt.UtcZone);

    [Fact]
    public async Task EverySetting_RoundTrips_OnAPresetAndOnAContract()
    {
        var preset = ContractPreset.Create(Guid.NewGuid(), "Preset", EverySettingOffDefault, null);
        var contract = Sealed(EverySettingOffDefault);
        Context.AddRange(preset, contract);
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        (await fresh.ContractPresets.SingleAsync(p => p.Id == preset.Id)).Rules.Should().Be(EverySettingOffDefault);
        (await fresh.NenContracts.SingleAsync(c => c.Id == contract.Id)).Rules.Should().Be(EverySettingOffDefault);
    }

    [Fact]
    public async Task NoFixedFloor_RoundTripsAsNone_AndTheFloorRuleFollowsTheStrictness()
    {
        var followsPlan = Sealed(TestRules.Of(StrictnessRule.Locked));
        var periodTarget = Sealed(TestRules.Of(StrictnessRule.PeriodTimeBudget));
        Context.AddRange(followsPlan, periodTarget);
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var reloadedPlan = await fresh.NenContracts.SingleAsync(c => c.Id == followsPlan.Id);
        var reloadedPeriod = await fresh.NenContracts.SingleAsync(c => c.Id == periodTarget.Id);
        reloadedPlan.Rules.FixedDailyFloor.Should().BeNull();
        reloadedPlan.FloorRule.Should().Be(new DailyFloorRule.FollowsPlan());
        reloadedPeriod.FloorRule.Should().Be(new DailyFloorRule.None());
    }

    [Fact]
    public async Task OneRulesObjectGivenToSeveralOwners_IsSavedOnEachOfThem()
    {
        // The wizard hands a preset's rules to a contract; two contracts may be sealed from the
        // same value. Each owner keeps its own copy (Gap 97's lesson).
        var first = Sealed(EverySettingOffDefault);
        var second = Sealed(EverySettingOffDefault);
        var preset = ContractPreset.Create(Guid.NewGuid(), "Preset", EverySettingOffDefault, null);
        Context.AddRange(first, second, preset);
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        (await fresh.NenContracts.SingleAsync(c => c.Id == first.Id)).Rules.Should().Be(EverySettingOffDefault);
        (await fresh.NenContracts.SingleAsync(c => c.Id == second.Id)).Rules.Should().Be(EverySettingOffDefault);
        (await fresh.ContractPresets.SingleAsync(p => p.Id == preset.Id)).Rules.Should().Be(EverySettingOffDefault);
    }

    [Fact]
    public async Task UpdatingAPresetsRules_IsSaved()
    {
        var preset = ContractPreset.Create(Guid.NewGuid(), "Preset", TestRules.Of(), null);
        Context.Add(preset);
        await Context.SaveChangesAsync();

        await using (var edit = CreateFreshContext())
        {
            var tracked = await edit.ContractPresets.SingleAsync(p => p.Id == preset.Id);
            tracked.Update("Preset", EverySettingOffDefault, null);
            await edit.SaveChangesAsync();
        }

        await using var fresh = CreateFreshContext();
        (await fresh.ContractPresets.SingleAsync(p => p.Id == preset.Id)).Rules.Should().Be(EverySettingOffDefault);
    }

    [Fact]
    public async Task Migration_TurnsAStoredFloorRuleIntoAFixedFloorOrNone_AndLeavesTheToggleOff()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new NenFocusDbContext(
            new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(connection).Options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20261002140517_SeedContractPresets");
        const string days = "[{\"Date\":\"2026-01-02\",\"WindowStart\":\"2026-01-02T00:00:00\",\"WindowEnd\":\"2026-01-03T00:00:00\",\"Floor\":null}]";
        const string insert =
            "INSERT INTO NenContracts (Id, Name, Goal, StartDate, EndDate, OriginalEndDate, StrictnessRule, Status, AllowCancellation, " +
            "RedemptionPlacementPolicy, KeepBlockingDuringBreaks, Days, FloorRule, CommittedFocusHours) " +
            "VALUES ({0}, {1}, 'Goal', '2026-01-02', '2026-01-02', '2026-01-02', {2}, 'Completed', 1, 'CurrentDayOnly', 1, {3}, {4}, 0);";
        var fixedId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var noneId = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(insert, fixedId, "fixed", "Locked", days, "{\"Kind\":\"Fixed\",\"Hours\":2.5}");
        await context.Database.ExecuteSqlRawAsync(insert, planId, "plan", "DailyFlexibility", days, "{\"Kind\":\"FollowsPlan\",\"Hours\":null}");
        await context.Database.ExecuteSqlRawAsync(insert, noneId, "none", "PeriodTimeBudget", days, "{\"Kind\":\"None\",\"Hours\":null}");

        await migrator.MigrateAsync();

        async Task<NenContract> ContractAsync(Guid id) => await context.NenContracts.AsNoTracking().SingleAsync(c => c.Id == id);
        var withFixed = await ContractAsync(fixedId);
        withFixed.Rules.Should().Be(new ContractRules(StrictnessRule.Locked, new DailyFloor(2.5), allowCancellation: true,
            keepBlockingDuringBreaks: true, RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: false));
        (await ContractAsync(planId)).FloorRule.Should().Be(new DailyFloorRule.FollowsPlan());
        (await ContractAsync(noneId)).FloorRule.Should().Be(new DailyFloorRule.None());

        // The seeded presets keep their settings, with the new toggle off.
        var lockdown = await context.ContractPresets.AsNoTracking().SingleAsync(p => p.Name == "Lockdown");
        lockdown.Rules.Should().Be(new ContractRules(StrictnessRule.Locked, new DailyFloor(4), allowCancellation: false,
            keepBlockingDuringBreaks: true, RedemptionPlacementPolicy.AnywhereInContract, bonusBlocksFollowStrictness: false));
    }
}
