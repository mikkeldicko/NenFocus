using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>The contract lifecycle sweep, run as the heartbeat runs it: real pipeline, real SQLite (Gap 110).</summary>
public class SweepNenContractLifecycleCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    public SweepNenContractLifecycleCommandHandlerTests() => Clock.SetNow(Now);

    private static NenContract Contract(DateOnly startDate, DateOnly endDate, DateOnly sealedOn) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(startDate, endDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(sealedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    /// <summary>Sealed the day before it starts, so it is Scheduled.</summary>
    private static NenContract ScheduledContract(DateOnly startDate) => Contract(startDate, startDate.AddDays(6), sealedOn: startDate.AddDays(-1));

    /// <summary>Sealed on its first day, so it is Active.</summary>
    private static NenContract ActiveContract(DateOnly startDate, DateOnly endDate) => Contract(startDate, endDate, sealedOn: startDate);

    private Task<SweepNenContractLifecycleResult> SweepAsync() => RunAsync(new SweepNenContractLifecycleCommand());

    private async Task<NenContractStatus> StoredStatusAsync(NenContract contract) =>
        (await FindAsync<NenContract>(contract.Id))!.Status;

    [Fact]
    public async Task NothingDue_ChangesNothing()
    {
        var contract = ScheduledContract(Today.AddDays(3));
        await SeedAsync(contract);

        var result = await SweepAsync();

        result.ActivatedContractIds.Should().BeEmpty();
        result.CompletedContractIds.Should().BeEmpty();
        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Scheduled);
    }

    [Fact]
    public async Task AContractDueToStart_IsStoredActive_AndReported()
    {
        var contract = ScheduledContract(Today);
        await SeedAsync(contract);

        var result = await SweepAsync();

        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Active);
        result.ActivatedContractIds.Should().Equal(contract.Id);
    }

    [Fact]
    public async Task AContractDueToEnd_IsStoredCompleted_AndItsRecapIsShown()
    {
        // Gap 110: the recap follows the saved NenContractCompletedEvent — the heartbeat no longer builds it.
        var contract = ActiveContract(Today.AddDays(-10), Today.AddDays(-1));
        await SeedAsync(contract);
        var shown = new List<NenContractRecapDto>();
        Resolve<IRecapRelay>().PostContractsReady += (_, recaps) => shown.AddRange(recaps);

        var result = await SweepAsync();

        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Completed);
        result.CompletedContractIds.Should().Equal(contract.Id);
        shown.Should().ContainSingle().Which.ContractId.Should().Be(contract.Id);
    }

    [Fact]
    public async Task StartsAndEndsDue_AreBothHandledInOneSweep()
    {
        var toActivate = ScheduledContract(Today);
        var toComplete = ActiveContract(Today.AddDays(-10), Today.AddDays(-1));
        await SeedAsync(toActivate, toComplete);

        var result = await SweepAsync();

        result.ActivatedContractIds.Should().Equal(toActivate.Id);
        result.CompletedContractIds.Should().Equal(toComplete.Id);
    }

    [Fact]
    public async Task AWholePeriodAlreadyOver_IsActivatedAndCompletedInOneSweep()
    {
        // The app was closed for the contract's whole period.
        var contract = ScheduledContract(Today.AddDays(-10));
        await SeedAsync(contract);

        var result = await SweepAsync();

        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Completed);
        result.ActivatedContractIds.Should().ContainSingle();
        result.CompletedContractIds.Should().ContainSingle();
    }

    // Regression (Gap 65): the sweep compared the UTC date against a contract's local dates,
    // so at UTC+2 a contract turned Active ~2 hours late (still "Upcoming" at 00:28 local
    // while its block ran) and completed ~2 hours late.

    [Fact]
    public async Task JustAfterLocalMidnightAtUtcPlus2_AContractStartingThatDay_IsActivated()
    {
        var localStartDay = new DateOnly(2026, 1, 5);
        Clock.SetNow(new DateTime(2026, 1, 4, 22, 28, 0, DateTimeKind.Utc)); // 00:28 local on Jan 5
        Clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        var contract = ScheduledContract(localStartDay);
        await SeedAsync(contract);

        await SweepAsync();

        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task JustAfterLocalMidnightAtUtcPlus2_AContractThatEndedYesterday_IsCompleted()
    {
        var localToday = new DateOnly(2026, 1, 5);
        Clock.SetNow(new DateTime(2026, 1, 4, 22, 28, 0, DateTimeKind.Utc)); // 00:28 local on Jan 5
        Clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        var contract = ActiveContract(localToday.AddDays(-7), localToday.AddDays(-1));
        await SeedAsync(contract);

        await SweepAsync();

        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Completed);
    }
}
