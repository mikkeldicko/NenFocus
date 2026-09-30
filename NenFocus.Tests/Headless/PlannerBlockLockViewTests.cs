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
/// Gap 88 phase 0: a started block is fully locked in the Planner — no drag of any edge, no
/// editable form, no Delete while it runs. The lock is read off the DTO's HasStarted.
/// </summary>
public class PlannerBlockLockViewTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    [AvaloniaFact]
    public async Task StartedBlockIsFullyLocked()
    {
        var block = Block(hasStarted: true);
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        vm.IsTileFullyLocked(block.Id).Should().BeTrue("no edge of a started tile may be dragged");

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.IsBlockLocked.Should().BeTrue();
        vm.CanDeleteFromForm.Should().BeFalse("a running block can't be deleted until it ends");
    }

    [AvaloniaFact]
    public async Task FutureBlockIsEditableAndDeletable()
    {
        var block = Block(hasStarted: false);
        var vm = CreatePlanner(block);
        Show(new PlannerPageView { DataContext = vm });

        vm.IsTileFullyLocked(block.Id).Should().BeFalse();

        await vm.EditBlockCommand.ExecuteAsync(vm.Tiles.Single());
        vm.IsBlockLocked.Should().BeFalse();
        vm.CanDeleteFromForm.Should().BeTrue();
    }

    private static PlannedBlockDto Block(bool hasStarted) => new()
    {
        Id = Guid.NewGuid(),
        PresetId = Guid.NewGuid(),
        PresetName = "Background",
        BlockPresetType = BlockPresetType.Background,
        Shown = At.Slot(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 11, 0, 0, DateTimeKind.Utc)),
        HasStarted = hasStarted,
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
