using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>Breaking a contract, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class BreakNenContractCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    public BreakNenContractCommandHandlerTests() => Clock.SetNow(Now);

    /// <summary>Active from its first day unless sealed earlier (then Scheduled until it starts).</summary>
    private static NenContract CancellableContract(bool allowCancellation = true, DateOnly? sealedOn = null) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: allowCancellation,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc((sealedOn ?? StartDate).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private static FocusBlock Member(NenContract contract, DateTime start, DateTime end, ContractMembership? membership = null) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(start, end), membership: membership ?? TestContracts.Committed(contract.Id));

    private Task<BreakNenContractResult> BreakAsync(Guid contractId) =>
        RunAsync(new BreakNenContractCommand { ContractId = contractId });

    [Fact]
    public async Task ContractNotFound_Fails()
    {
        var result = await BreakAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyContractId_IsRefused()
    {
        var result = await BreakAsync(Guid.Empty);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AScheduledContract_CanBeBrokenBeforeItsFirstDay()
    {
        Clock.SetNow(StartDate.AddDays(-2).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var contract = CancellableContract(sealedOn: StartDate.AddDays(-5));
        await SeedAsync(contract);

        var result = await BreakAsync(contract.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<NenContract>(contract.Id))!.Status.Should().Be(NenContractStatus.Broken);
    }

    [Fact]
    public async Task ACompletedContract_IsRefused()
    {
        var contract = CancellableContract();
        contract.Complete(At.Utc(Now));
        await SeedAsync(contract);

        var result = await BreakAsync(contract.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<NenContract>(contract.Id))!.Status.Should().Be(NenContractStatus.Completed);
    }

    [Fact]
    public async Task SealedWithoutCancellation_IsRefused_SayingSo()
    {
        // The contract's own setting is the gate (decided 2026-10-06, ending the 2026-08-18 interim).
        var contract = CancellableContract(allowCancellation: false);
        await SeedAsync(contract);

        var result = await BreakAsync(contract.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sealed without cancellation");
    }

    [Fact]
    public async Task ValidContract_IsStoredBroken_EndingOnTheBreakDay()
    {
        var contract = CancellableContract();
        await SeedAsync(contract);

        var result = await BreakAsync(contract.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await FindAsync<NenContract>(contract.Id);
        stored!.Status.Should().Be(NenContractStatus.Broken);
        stored.EndDate.Should().Be(DateOnly.FromDateTime(Now));
    }

    [Fact]
    public async Task CancelsOnlyBlocksThatHaveNotEnded()
    {
        var contract = CancellableContract();
        var pastBlock = Member(contract, Now.AddHours(-3), Now.AddHours(-1));     // already ended (missed)
        var currentBlock = Member(contract, Now.AddHours(-1), Now.AddHours(1));   // active right now
        var futureBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1)), membership: TestContracts.Committed(contract.Id));
        await SeedAsync(contract, pastBlock, currentBlock, futureBlock);

        await BreakAsync(contract.Id);

        (await FindAsync<Block>(pastBlock.Id))!.CancelledAt.Should().BeNull();
        (await FindAsync<Block>(currentBlock.Id))!.CancelledAt.Should().Be(At.Utc(Now));
        (await FindAsync<Block>(futureBlock.Id))!.CancelledAt.Should().Be(At.Utc(Now));
    }

    [Fact]
    public async Task ABlockWhoseSessionEndedEarly_IsLeftUncancelled()
    {
        // Its raw End is still ahead, but it already has a resolved outcome.
        var contract = CancellableContract();
        var endedEarly = Member(contract, Now.AddMinutes(-30), Now.AddHours(1));
        endedEarly.MarkRitualCompleted(At.Utc(Now.AddMinutes(-30)));
        endedEarly.MarkSessionEnded(At.Utc(Now.AddMinutes(-5)));
        await SeedAsync(contract, endedEarly);

        await BreakAsync(contract.Id);

        (await FindAsync<Block>(endedEarly.Id))!.CancelledAt.Should().BeNull();
    }

    [Fact]
    public async Task BonusBlocks_NotStartedIsReleased_StartedKeepsItsMembership()
    {
        // Gap 97: the handler passes "keep" until Gap 108 adds the prompt — today's behaviour.
        var contract = CancellableContract();
        var upcomingBonus = Member(contract, Now.AddHours(2), Now.AddHours(3), TestContracts.Bonus(contract.Id));
        var runningBonus = Member(contract, Now.AddHours(-1), Now.AddHours(1), TestContracts.Bonus(contract.Id));
        await SeedAsync(contract, upcomingBonus, runningBonus);

        await BreakAsync(contract.Id);

        var upcoming = await FindAsync<Block>(upcomingBonus.Id);
        upcoming!.Membership.Should().BeNull();
        upcoming.CancelledAt.Should().BeNull();
        var running = await FindAsync<Block>(runningBonus.Id);
        running!.Membership.Should().Be(TestContracts.Bonus(contract.Id));
        running.CancelledAt.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]  // the session of the contract's own running block
    [InlineData(false)] // any other session
    public async Task WhileASessionRuns_IsRefused_AndNothingChanges(bool sessionOfTheContractsBlock)
    {
        // Gap 95: no contract ends while Focus time is being worked.
        var contract = CancellableContract();
        var currentBlock = Member(contract, Now.AddHours(-1), Now.AddHours(1));
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), sessionOfTheContractsBlock ? SessionOrigin.Planned : SessionOrigin.Spontaneous,
            originatingBlockId: sessionOfTheContractsBlock ? currentBlock.Id : null);
        session.Start(At.Utc(Now.AddMinutes(-10)));
        var statusBefore = contract.Status;
        await SeedAsync(contract, currentBlock, session);
        var shown = RecapsShown();

        var result = await BreakAsync(contract.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("session is running");
        (await FindAsync<NenContract>(contract.Id))!.Status.Should().Be(statusBefore);
        (await FindAsync<Block>(currentBlock.Id))!.CancelledAt.Should().BeNull();
        (await FindAsync<Session>(session.Id))!.Status.Should().Be(SessionStatus.Active);
        shown.Should().BeEmpty("a refused break owes no recap");
    }

    [Fact]
    public async Task ItsRecapIsShown_OnceTheBreakIsSaved()
    {
        // Gap 110: the recap follows the saved NenContractBrokenEvent, not the command.
        var contract = CancellableContract();
        var upcomingBonus = Member(contract, Now.AddHours(2), Now.AddHours(3), TestContracts.Bonus(contract.Id));
        await SeedAsync(contract, upcomingBonus);
        var shown = RecapsShown();

        await BreakAsync(contract.Id);

        shown.Should().ContainSingle().Which.ContractId.Should().Be(contract.Id);
    }

    private List<NenContractRecapDto> RecapsShown()
    {
        var shown = new List<NenContractRecapDto>();
        Resolve<IRecapRelay>().PostContractsReady += (_, recaps) => shown.AddRange(recaps);
        return shown;
    }
}
