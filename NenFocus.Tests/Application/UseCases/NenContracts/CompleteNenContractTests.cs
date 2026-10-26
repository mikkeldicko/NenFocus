using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.CompleteNenContract;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>
/// Completing a contract early, run as the app runs it: real pipeline, real SQLite (Gap 110). The
/// contract answers whether it may complete (NenContract.CheckComplete, Gap 92 D); the validator
/// words its refusal, and the handler asks it again so no caller can skip it.
/// </summary>
public class CompleteNenContractTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10); // days left — completing now ends it early

    public CompleteNenContractTests() => Clock.SetNow(Now);

    private static NenContract ActiveContract(double committedHours = 0) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private Task<CompleteNenContractResult> CompleteAsync(Guid contractId) =>
        RunAsync(new CompleteNenContractCommand { ContractId = contractId });

    private async Task<NenContractStatus> StoredStatusAsync(NenContract contract) =>
        (await FindAsync<NenContract>(contract.Id))!.Status;

    [Fact]
    public async Task Ready_IsStoredCompleted_AndItsRecapIsShown()
    {
        var contract = ActiveContract();
        await SeedAsync(contract);
        var shown = new List<NenContractRecapDto>();
        Resolve<IRecapRelay>().PostContractsReady += (_, recaps) => shown.AddRange(recaps);

        var result = await CompleteAsync(contract.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Completed);
        shown.Should().ContainSingle().Which.ContractId.Should().Be(contract.Id);
    }

    [Fact]
    public async Task ContractNotFound_Fails()
    {
        var result = await CompleteAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ABrokenContract_IsRefused()
    {
        var contract = ActiveContract();
        contract.Break(At.Utc(Now), At.UtcZone, sessionRunning: false);
        await SeedAsync(contract);

        var result = await CompleteAsync(contract.Id);

        result.Success.Should().BeFalse();
        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Broken);
    }

    [Fact]
    public async Task WhileASessionRuns_IsRefused_WithItsReason_AndNothingChanges()
    {
        var contract = ActiveContract();
        var running = Session.Start(Guid.NewGuid(), new SessionGoal("Focus"),
            FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now, Now.AddHours(1))),
            At.Utc(Now));
        await SeedAsync(contract, running);

        var result = await CompleteAsync(contract.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ContractActionVerdictDto.From(ContractActionVerdict.Refused(ContractActionOutcome.SessionIsRunning)).Message);
        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task HoursStillOwed_IsRefused_SayingHowMany()
    {
        var contract = ActiveContract(committedHours: 4);
        await SeedAsync(contract);

        var result = await CompleteAsync(contract.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("4h remaining");
        (await StoredStatusAsync(contract)).Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task ABlockStillScheduled_IsRefused()
    {
        var contract = ActiveContract();
        var upcoming = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1)),
            membership: TestContracts.Bonus(contract.Id));
        await SeedAsync(contract, upcoming);

        var result = await CompleteAsync(contract.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("still a block scheduled");
    }
}
