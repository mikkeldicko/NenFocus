using Avalonia.Headless.XUnit;
using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.ViewModels.Pages;
using NenFocus.Views;
using NSubstitute;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// Gap 93: the Planner offers a drag, a field or a button from the block DTO's Allowances and
/// never works a lock out itself — so it can't hide what would work or show what will always be
/// refused. (Which blocks get which allowances is BlockChangeChecker's, tested in the Domain.)
/// </summary>
public class PlannerBlockLockViewTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    [AvaloniaFact]
    public async Task ABlockThatAllowsNothing_IsFullyLocked()
    {
        // A started block, or one bound by a Lockdown contract.
        var block = Block(new BlockChangeAllowances(CanReschedule: false, CanChangePreset: false, CanSplit: false, CanDelete: false));
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        vm.IsTileFullyLocked(block.Id).Should().BeTrue("no edge of the tile may be dragged");

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.CanRescheduleBlock.Should().BeFalse();
        vm.CanChangePreset.Should().BeFalse();
        vm.CanSaveBlock.Should().BeFalse("there is nothing to save");
        vm.CanSplitBlock.Should().BeFalse();
        vm.CanDeleteFromForm.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AFreeBlock_IsEditableAndDeletable_ButNotSplittable()
    {
        // A plain block, or a Bonus block its contract doesn't bind.
        var block = Block(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: true, CanSplit: false, CanDelete: true));
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        vm.IsTileFullyLocked(block.Id).Should().BeFalse();

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.CanRescheduleBlock.Should().BeTrue();
        vm.CanChangePreset.Should().BeTrue();
        vm.CanSaveBlock.Should().BeTrue();
        vm.CanSplitBlock.Should().BeFalse();
        vm.CanDeleteFromForm.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ABoundBlockThatMayMove_CanBeRescheduledAndSplit_ButKeepsItsPresetAndCannotBeDeleted()
    {
        // A committed block under Daily Flex or Period Study.
        var block = Block(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: false, CanSplit: true, CanDelete: false));
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        vm.IsTileFullyLocked(block.Id).Should().BeFalse();

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.CanRescheduleBlock.Should().BeTrue();
        vm.CanChangePreset.Should().BeFalse();
        vm.CanSaveBlock.Should().BeTrue();
        vm.CanSplitBlock.Should().BeTrue();
        vm.CanDeleteFromForm.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task SplitDialog_AsksForADayPerPiece_OnlyWhenAPieceMayChangeDay_AndSendsTheChosenDays()
    {
        // A committed block under Period Study: a piece may land on another contract day.
        var block = Block(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: false, CanSplit: true, CanDelete: false)
        {
            SplitPiecesMayChangeDay = true
        });
        var commands = Substitute.For<ICommandDispatcher>();
        SplitPlannedBlockCommand? sent = null;
        commands.DispatchAsync(Arg.Do<SplitPlannedBlockCommand>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(SplitPlannedBlockResult.Ok(Guid.NewGuid(), Guid.NewGuid()));
        var vm = CreatePlanner(block, commands);
        Show(new PlannerPageView { DataContext = vm });

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.OpenSplitDialogCommand.Execute(null);
        vm.ShowSplitDates.Should().BeTrue();
        vm.SplitSecondDate = vm.SplitSecondDate.AddDays(1);
        await vm.ConfirmSplitCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.First.Should().Be(At.Slot(new DateTime(2026, 9, 29, 10, 0, 0), new DateTime(2026, 9, 29, 10, 30, 0)));
        sent.Second.Should().Be(At.Slot(new DateTime(2026, 9, 30, 10, 30, 0), new DateTime(2026, 9, 30, 11, 0, 0)));
    }

    [AvaloniaFact]
    public async Task SplitDialog_WithoutThatAllowance_ShowsNoDays()
    {
        // A committed block under Daily Flex: both pieces stay on its own day.
        var block = Block(new BlockChangeAllowances(CanReschedule: true, CanChangePreset: false, CanSplit: true, CanDelete: false));
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.OpenSplitDialogCommand.Execute(null);

        vm.ShowSplitDates.Should().BeFalse();
    }

    private static PlannedBlockDto Block(BlockChangeAllowances allowances) => new()
    {
        Id = Guid.NewGuid(),
        PresetId = Guid.NewGuid(),
        PresetName = "Deep work",
        BlockPresetType = BlockPresetType.Focus,
        Shown = At.Slot(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 11, 0, 0, DateTimeKind.Utc)),
        Allowances = allowances,
    };

    private static PlannerPageViewModel CreatePlanner(PlannedBlockDto block, ICommandDispatcher? commands = null)
    {
        var queries = new FakeQueryDispatcher().Answer<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>(q =>
            q.Date == Today ? [block] : []);
        var clock = Substitute.For<IPlannerClockService>();
        var systemClock = TestClock.Create();
        systemClock.SetTimeZoneOffset(TimeSpan.Zero);
        systemClock.SetNow(new DateTime(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc));
        return new PlannerPageViewModel(queries, commands ?? Substitute.For<ICommandDispatcher>(), clock, Substitute.For<ISessionStateService>(), systemClock);
    }
}
