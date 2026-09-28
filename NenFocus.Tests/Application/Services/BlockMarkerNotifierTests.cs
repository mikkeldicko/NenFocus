using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Application.Services;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class BlockMarkerNotifierTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();

    private BlockMarkerNotifier CreateNotifier() => new(_blocks, _presets, _notifier);

    // Every block's preset is called "Deep Work" unless a test says otherwise.
    public BlockMarkerNotifierTests() =>
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => BlockPreset.Create(ci.Arg<Guid>(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Start));

    private void GivenBlocks(params PlannedBlock[] blocks) =>
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(blocks);

    private static FocusBlock Focus(DateTime start, string? title = null) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1), title);

    private static BackgroundBlock Background(DateTime start, string? title = null) =>
        BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1), title);

    [Fact]
    public async Task NotifyPassedAsync_WarningMomentCrossed_ReportsStartingSoonWithMinutesLeft()
    {
        GivenBlocks(Focus(Start, "Thesis"));

        await CreateNotifier().NotifyPassedAsync(Start.AddMinutes(-5).AddSeconds(-5), Start.AddMinutes(-5));

        await _notifier.Received(1).NotifyAsync(
            new BlockStartingSoon(BlockPresetType.Focus, "Thesis", 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPassedAsync_StartCrossed_ReportsStarted()
    {
        GivenBlocks(Background(Start, "Socials off"));

        await CreateNotifier().NotifyPassedAsync(Start.AddSeconds(-5), Start);

        await _notifier.Received(1).NotifyAsync(
            new BlockStarted(BlockPresetType.Background, "Socials off"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPassedAsync_EndCrossed_ReportsEnded()
    {
        GivenBlocks(Background(Start));

        await CreateNotifier().NotifyPassedAsync(Start.AddHours(1).AddSeconds(-5), Start.AddHours(1));

        await _notifier.Received(1).NotifyAsync(
            new BlockEnded(BlockPresetType.Background, "Deep Work"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyPassedAsync_OneBlockEndsAsAnotherStarts_ReportsInTimeOrder()
    {
        var ending = Background(Start.AddHours(-1));
        var starting = Focus(Start);
        GivenBlocks(starting, ending);

        await CreateNotifier().NotifyPassedAsync(Start.AddSeconds(-5), Start);

        Received.InOrder(() =>
        {
            _notifier.NotifyAsync(new BlockEnded(BlockPresetType.Background, "Deep Work"), Arg.Any<CancellationToken>());
            _notifier.NotifyAsync(new BlockStarted(BlockPresetType.Focus, "Deep Work"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task NotifyPassedAsync_NothingCrossed_ReportsNothing()
    {
        GivenBlocks(Focus(Start.AddMinutes(30)));

        await CreateNotifier().NotifyPassedAsync(Start.AddSeconds(-5), Start);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyAsync(default!);
    }

    [Fact]
    public async Task NotifyPassedAsync_FirstTick_EmptyWindow_DoesNotEvenFetch()
    {
        // The heartbeat's first tick passes (now, now]: nothing is announced late.
        await CreateNotifier().NotifyPassedAsync(Start, Start);

        await _blocks.DidNotReceiveWithAnyArgs().GetForRangeAsync(default, default);
    }

    [Fact]
    public async Task NotifyPassedAsync_FetchesEveryBlockThatCouldHaveCrossed()
    {
        GivenBlocks();
        var from = Start.AddSeconds(-5);

        await CreateNotifier().NotifyPassedAsync(from, Start);

        await _blocks.Received(1).GetForRangeAsync(
            from, Start + PlannedBlock.UpcomingWarningLead, Arg.Any<CancellationToken>());
    }
}
