using FluentAssertions;
using NenFocus.Application.UseCases.NenContracts.Delete;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>
/// Deleting a contract, run as the app runs it: real pipeline, real SQLite (Gap 110). Only a
/// contract that hasn't started may go (NenContract.CheckDelete); its drafted blocks go with it,
/// a Bonus block stays as a plain block (Gap 97).
/// </summary>
public class DeleteNenContractTests : UseCaseTestBase
{
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly EndDate = new(2026, 6, 7);

    public DeleteNenContractTests() => Clock.SetNow(StartDate.AddDays(-3).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

    private static NenContract Contract(DateOnly sealedOn) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Summer Focus",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(sealedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private static NenContract ScheduledContract() => Contract(sealedOn: StartDate.AddDays(-10));

    private static TimeSlot On(int fromHour, int toHour) =>
        At.Slot(StartDate.ToDateTime(new TimeOnly(fromHour, 0)), StartDate.ToDateTime(new TimeOnly(toHour, 0)));

    private Task<DeleteNenContractResult> DeleteAsync(Guid contractId) =>
        RunAsync(new DeleteNenContractCommand { ContractId = contractId });

    [Fact]
    public async Task AScheduledContract_IsRemoved_WithItsDraftedBlocks()
    {
        var contract = ScheduledContract();
        var focus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(9, 10), membership: TestContracts.Committed(contract.Id));
        var background = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(11, 12), membership: TestContracts.Committed(contract.Id));
        await SeedAsync(contract, focus, background);

        var result = await DeleteAsync(contract.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<NenContract>(contract.Id)).Should().BeNull();
        (await FindAsync<PlannedBlock>(focus.Id)).Should().BeNull();
        (await FindAsync<PlannedBlock>(background.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ABonusBlock_StaysAsAPlainBlock()
    {
        // Gap 97: a Bonus block was added on its own, not drafted — deleting the contract never destroys it.
        var contract = ScheduledContract();
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(13, 14), membership: TestContracts.Bonus(contract.Id));
        await SeedAsync(contract, bonus);

        await DeleteAsync(contract.Id);

        var stored = await FindAsync<PlannedBlock>(bonus.Id);
        stored.Should().NotBeNull();
        stored!.Membership.Should().BeNull();
    }

    [Fact]
    public async Task AlreadyGone_Succeeds()
    {
        // Gap 110: removing is idempotent — "Contract not found." is gone.
        var result = await DeleteAsync(Guid.NewGuid());

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task EmptyContractId_IsRefused()
    {
        var result = await DeleteAsync(Guid.Empty);

        result.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)] // active
    [InlineData(true)]  // completed
    public async Task AContractThatHasStarted_IsRefused_AndKept(bool completed)
    {
        Clock.SetNow(EndDate.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var contract = Contract(sealedOn: StartDate); // Active from the start
        if (completed) contract.Complete(At.Utc(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        await SeedAsync(contract);

        var result = await DeleteAsync(contract.Id);

        result.Success.Should().BeFalse();
        (await FindAsync<NenContract>(contract.Id)).Should().NotBeNull();
    }
}
