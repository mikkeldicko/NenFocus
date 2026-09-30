using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 93 finding 1: the Split button is offered for every strictness rule the Split validator
/// accepts — Daily Flex and Period Study, never Lockdown. Stands in until Gap 93 replaces
/// CanSplitBlock with the block DTO's CanSplit flag; then repoint it there.
/// </summary>
public class PlannerSplitButtonViewTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    [AvaloniaTheory]
    [InlineData(StrictnessRule.DailyFlexibility, true)]
    [InlineData(StrictnessRule.PeriodTimeBudget, true)]
    [InlineData(StrictnessRule.Locked, false)]
    public async Task SplitIsOfferedWhenTheContractAllowsIt(StrictnessRule strictness, bool offered)
    {
        var block = ContractFocusBlock(strictness);
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());

        vm.CanSplitBlock.Should().Be(offered);
    }

    private static PlannedBlockDto ContractFocusBlock(StrictnessRule strictness) => new()
    {
        Id = Guid.NewGuid(),
        PresetId = Guid.NewGuid(),
        PresetName = "Deep work",
        BlockPresetType = BlockPresetType.Focus,
        Shown = At.Slot(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 11, 0, 0, DateTimeKind.Utc)),
        ContractId = Guid.NewGuid(),
        IsUnderInEffectContract = true,
        ContractStrictnessRule = strictness,
    };

    private static PlannerPageViewModel CreatePlanner(PlannedBlockDto block)
    {
        var queries = new FakeQueryDispatcher().Answer<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>(q =>
            q.Date == Today ? [block] : []);
        var clock = Substitute.For<IPlannerClockService>();
        var systemClock = TestClock.Create();
        systemClock.SetTimeZoneOffset(TimeSpan.Zero);
        systemClock.SetNow(new DateTime(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc));
        return new PlannerPageViewModel(queries, Substitute.For<ICommandDispatcher>(), clock, Substitute.For<ISessionStateService>(), systemClock);
    }
}
