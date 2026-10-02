using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class SweepNenContractLifecycleCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public SweepNenContractLifecycleCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        SetInEffect();
    }

    private SweepNenContractLifecycleCommandHandler CreateHandler() => new(_contracts, _clock);

    private static NenContract ScheduledContract(DateOnly startDate) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(startDate, startDate.AddDays(6), new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(startDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone); // seals as Scheduled

    private static NenContract ActiveContract(DateOnly startDate, DateOnly endDate)
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(startDate, endDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone); // seals as Active
        return contract;
    }

    private void SetInEffect(params NenContract[] contracts) =>
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)contracts);

    [Fact]
    public async Task HandleAsync_NothingDue_ReturnsEmptyResultAndSavesNothing()
    {
        SetInEffect(ScheduledContract(DateOnly.FromDateTime(Now).AddDays(3)));

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        result.ActivatedContractIds.Should().BeEmpty();
        result.CompletedContractIds.Should().BeEmpty();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractDueForActivation_ActivatesSavesAndReportsIt()
    {
        var contract = ScheduledContract(DateOnly.FromDateTime(Now));
        contract.Status.Should().Be(NenContractStatus.Scheduled);
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Active);
        result.ActivatedContractIds.Should().ContainSingle().Which.Should().Be(contract.Id);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ContractDueForCompletion_CompletesSavesAndReportsIt()
    {
        var contract = ActiveContract(DateOnly.FromDateTime(Now).AddDays(-10), DateOnly.FromDateTime(Now).AddDays(-1));
        contract.Status.Should().Be(NenContractStatus.Active);
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Completed);
        result.CompletedContractIds.Should().ContainSingle().Which.Should().Be(contract.Id);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BothActivationsAndCompletionsDue_HandlesBothInOneSweep()
    {
        var toActivate = ScheduledContract(DateOnly.FromDateTime(Now));
        var toComplete = ActiveContract(DateOnly.FromDateTime(Now).AddDays(-10), DateOnly.FromDateTime(Now).AddDays(-1));
        SetInEffect(toActivate, toComplete);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        result.ActivatedContractIds.Should().ContainSingle().Which.Should().Be(toActivate.Id);
        result.CompletedContractIds.Should().ContainSingle().Which.Should().Be(toComplete.Id);
    }

    [Fact]
    public async Task HandleAsync_WholePeriodAlreadyOver_ActivatesAndCompletesInOneSweep()
    {
        // The app was closed for the contract's whole period.
        var contract = ScheduledContract(DateOnly.FromDateTime(Now).AddDays(-10));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Completed);
        result.ActivatedContractIds.Should().ContainSingle();
        result.CompletedContractIds.Should().ContainSingle();
    }

    // Regression (Gap 65): the sweep compared the UTC date against a contract's local dates,
    // so at UTC+2 a contract turned Active ~2 hours late (still "Upcoming" at 00:28 local
    // while its block ran) and completed ~2 hours late.

    [Fact]
    public async Task HandleAsync_JustAfterLocalMidnightAtUtcPlus2_ActivatesAContractStartingThatDay()
    {
        var localStartDay = new DateOnly(2026, 1, 5);
        _clock.SetNow(new DateTime(2026, 1, 4, 22, 28, 0, DateTimeKind.Utc)); // 00:28 local on Jan 5
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        var contract = ScheduledContract(localStartDay);
        SetInEffect(contract);

        await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_JustAfterLocalMidnightAtUtcPlus2_CompletesAContractThatEndedYesterday()
    {
        var localToday = new DateOnly(2026, 1, 5);
        _clock.SetNow(new DateTime(2026, 1, 4, 22, 28, 0, DateTimeKind.Utc)); // 00:28 local on Jan 5
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(2));
        var contract = ActiveContract(localToday.AddDays(-7), localToday.AddDays(-1));
        SetInEffect(contract);

        await CreateHandler().HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);

        contract.Status.Should().Be(NenContractStatus.Completed);
    }
}
