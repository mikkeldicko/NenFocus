using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 64: the rule was renamed and inverted (PauseEnforcementDuringBreak → KeepBlockingDuringBreaks)
/// and copied onto sessions. Its DB default matters too — EF omits a CLR-default value from INSERTs
/// and lets the column default apply, so a stale "true" default would silently flip every row.
/// </summary>
public class KeepBlockingDuringBreaksPersistenceTests : SqliteRepositoryTestBase
{
    private static readonly DateTime At = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContractPresetContractAndSession_RoundTripTheRule(bool keepBlocking)
    {
        var preset = ContractPreset.Create(Guid.NewGuid(), "Strict", StrictnessRule.Locked, null, null, false,
            keepBlockingDuringBreaks: keepBlocking);
        var contract = NenContract.Seal(
            id: Guid.NewGuid(), name: "Contract", goal: new NenContractGoal("Goal"),
            startDate: DateOnly.FromDateTime(At), endDate: DateOnly.FromDateTime(At).AddDays(7),
            strictnessRule: StrictnessRule.Locked, dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null,
            allowCancellation: false, plannedFocusHoursByDay: new Dictionary<DateOnly, double>(), sealedAt: At,
            keepBlockingDuringBreaks: keepBlocking);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"),
            new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At, keepBlockingDuringBreaks: keepBlocking);

        Context.AddRange(preset, contract, session);
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        (await fresh.ContractPresets.SingleAsync(p => p.Id == preset.Id)).KeepBlockingDuringBreaks.Should().Be(keepBlocking);
        (await fresh.NenContracts.SingleAsync(c => c.Id == contract.Id)).KeepBlockingDuringBreaks.Should().Be(keepBlocking);
        (await fresh.Sessions.SingleAsync(s => s.Id == session.Id)).KeepBlockingDuringBreaks.Should().Be(keepBlocking);
    }

    [Fact]
    public async Task Migration_InvertsExistingPresetValues_SoNoPresetLosesItsSetting()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new NenFocusDbContext(
            new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(connection).Options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20260927154029_TimeDrivenBreakSchedule");
        var strictId = Guid.NewGuid();
        var lenientId = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO ContractPresets (Id, Name, StrictnessRule, AllowCancellation, RedemptionPlacementPolicy, PauseEnforcementDuringBreak) " +
            "VALUES ({0}, 'Strict', 'Locked', 0, 'AnywhereInContract', 0), ({1}, 'Lenient', 'Locked', 0, 'AnywhereInContract', 1);",
            strictId, lenientId);

        await migrator.MigrateAsync();

        (await context.ContractPresets.SingleAsync(p => p.Id == strictId)).KeepBlockingDuringBreaks.Should().BeTrue();
        (await context.ContractPresets.SingleAsync(p => p.Id == lenientId)).KeepBlockingDuringBreaks.Should().BeFalse();
    }
}
