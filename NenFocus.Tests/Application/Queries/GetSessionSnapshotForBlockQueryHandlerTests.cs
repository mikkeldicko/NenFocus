using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetSessionSnapshotForBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetSessionSnapshotForBlockQueryHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockedAttemptTracker _attempts = Substitute.For<IBlockedAttemptTracker>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetSessionSnapshotForBlockQueryHandlerTests()
    {
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
    }

    private GetSessionSnapshotForBlockQueryHandler CreateHandler() => new(_sessions, _attempts, _blocks, _presets, _clock);

    private static Session CreateCompletedSession(Guid originatingBlockId, string goal = "Finish report", string? reflection = null)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal(goal),
            SessionDuration.FromHoursAndMinutes(1, 0),
            Guid.NewGuid(),
            SessionOrigin.Planned,
            originatingBlockId);
        session.Start(At.Utc(Start));
        session.Complete(At.Utc(End));
        if (reflection is not null) session.SetReflection(reflection);
        return session;
    }

    [Fact]
    public async Task HandleAsync_NoSessionForBlock_ReturnsNull()
    {
        var blockId = Guid.NewGuid();
        _sessions.GetByOriginatingBlockIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new GetSessionSnapshotForBlockQuery { PlannedBlockId = blockId });

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_SessionExists_MapsGoalReflectionAndAttempts()
    {
        var blockId = Guid.NewGuid();
        var session = CreateCompletedSession(blockId, "Finish report", "Went well.");
        _sessions.GetByOriginatingBlockIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(session);
        _attempts.GetAttemptsForSessionAsync(session.Id).Returns(
        [
            BlockedAttempt.Record(Guid.NewGuid(), session.Id, "chrome", BlockedAttemptType.App, At.Utc(Start.AddMinutes(5)))
        ]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(new GetSessionSnapshotForBlockQuery { PlannedBlockId = blockId });

        result.Should().NotBeNull();
        result!.Goal.Should().Be("Finish report");
        result.Reflection.Should().Be("Went well.");
        result.Attempts.Should().ContainSingle().Which.ResourceName.Should().Be("chrome");
        result.RedemptionCoverage.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BlockHasRedemptionEntries_MapsCoverageWithResolvedPresetAndDate()
    {
        var missedBlockId = Guid.NewGuid();
        var missedPresetId = Guid.NewGuid();
        var missedStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var missedBlock = FocusBlock.Create(missedBlockId, missedPresetId,At.Slot(missedStart, missedStart.AddHours(1)));

        var coveringBlockId = Guid.NewGuid();
        var coveringPresetId = Guid.NewGuid();
        var coveringBlock = FocusBlock.Create(coveringBlockId, coveringPresetId,At.Slot(Start, End),
            membership: TestContracts.Redemption(Guid.NewGuid()), redemptionEntries: [new RedemptionEntry(missedBlockId, 30)]);

        var session = CreateCompletedSession(coveringBlockId);
        _sessions.GetByOriginatingBlockIdAsync(coveringBlockId, Arg.Any<CancellationToken>()).Returns(session);
        _attempts.GetAttemptsForSessionAsync(session.Id).Returns([]);
        _blocks.GetByIdAsync(coveringBlockId, Arg.Any<CancellationToken>()).Returns(coveringBlock);
        _blocks.GetByIdAsync(missedBlockId, Arg.Any<CancellationToken>()).Returns(missedBlock);
        _presets.GetByIdAsync(missedPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(missedPresetId, "Deep Work", WebsiteFilterType.Allowlist, BlockPresetType.Focus, At.Utc(missedStart)));

        var result = await CreateHandler().HandleAsync(new GetSessionSnapshotForBlockQuery { PlannedBlockId = coveringBlockId });

        var coverage = result!.RedemptionCoverage.Should().ContainSingle().Subject;
        coverage.MissedBlockId.Should().Be(missedBlockId);
        coverage.MissedBlockPresetName.Should().Be("Deep Work");
        coverage.MissedBlockDate.Should().Be(DateOnly.FromDateTime(missedStart));
        coverage.MinutesCovered.Should().Be(30);
    }

    [Fact]
    public async Task HandleAsync_MissedBlockDate_UsesLocalCalendarDay_NotRawUtcDate()
    {
        var missedBlockId = Guid.NewGuid();
        var missedPresetId = Guid.NewGuid();
        // 1:00 AM UTC Jan 2 — but 8:00 PM local (UTC-5) on Jan 1.
        var missedStart = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc);
        var missedBlock = FocusBlock.Create(missedBlockId, missedPresetId,At.Slot(missedStart, missedStart.AddHours(1)));

        var coveringBlockId = Guid.NewGuid();
        var coveringBlock = FocusBlock.Create(coveringBlockId, Guid.NewGuid(),At.Slot(Start, End),
            membership: TestContracts.Redemption(Guid.NewGuid()), redemptionEntries: [new RedemptionEntry(missedBlockId, 30)]);

        var session = CreateCompletedSession(coveringBlockId);
        _sessions.GetByOriginatingBlockIdAsync(coveringBlockId, Arg.Any<CancellationToken>()).Returns(session);
        _attempts.GetAttemptsForSessionAsync(session.Id).Returns([]);
        _blocks.GetByIdAsync(coveringBlockId, Arg.Any<CancellationToken>()).Returns(coveringBlock);
        _blocks.GetByIdAsync(missedBlockId, Arg.Any<CancellationToken>()).Returns(missedBlock);
        _presets.GetByIdAsync(missedPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(missedPresetId, "Deep Work", WebsiteFilterType.Allowlist, BlockPresetType.Focus, At.Utc(missedStart)));
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));

        var result = await CreateHandler().HandleAsync(new GetSessionSnapshotForBlockQuery { PlannedBlockId = coveringBlockId });

        var coverage = result!.RedemptionCoverage.Should().ContainSingle().Subject;
        coverage.MissedBlockDate.Should().Be(new DateOnly(2026, 1, 1));
    }
}
