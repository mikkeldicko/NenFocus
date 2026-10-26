using FluentAssertions;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>
/// Marking a missed Focus block, run as the app runs it: real pipeline, real SQLite (Gap 110).
/// Kept minimal — Gap 114 replaces this command with the block's stored outcome.
/// </summary>
public class MarkFocusBlockMissedCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    public MarkFocusBlockMissedCommandHandlerTests() => Clock.SetNow(End.AddHours(1));

    private Task<MarkFocusBlockMissedResult> MarkAsync(Guid blockId) =>
        RunAsync(new MarkFocusBlockMissedCommand { FocusBlockId = blockId });

    [Fact]
    public async Task AnUnknownBlock_IsRefused_AndNoSessionIsStored()
    {
        var result = await MarkAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        (await ReadAsync(context => Task.FromResult(context.Sessions.Count()))).Should().Be(0);
    }

    [Fact]
    public async Task StoresAMissedSessionFromTheBlocksSchedule()
    {
        var presetId = await FocusPresetAsync("Deep Work");
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, At.Slot(Start, End));
        await SeedAsync(block);

        var result = await MarkAsync(block.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var session = (await FindAsync<Session>(result.SessionId!.Value))!;
        session.Origin.Should().Be(SessionOrigin.Planned);
        session.OriginatingBlockId.Should().Be(block.Id);
        session.BlockPresetId.Should().Be(presetId);
        session.Status.Should().Be(SessionStatus.Missed);
        session.StartedAt.Should().Be(At.Utc(Start));
        session.EndedAt.Should().Be(At.Utc(End));
        session.Duration.Value.Should().Be(TimeSpan.FromHours(1));
        session.Goal.Value.Should().Be("(missed — no goal set)");

        result.PostSession!.Status.Should().Be(SessionStatus.Missed);
        result.PostSession.PresetName.Should().Be("Deep Work");
        result.PostSession.Recorded.Should().Be(At.Slot(Start, End));
        result.PostSession.PlannedDurationMinutes.Should().Be(60);
        result.PostSession.ActualDurationMinutes.Should().Be(0);
        result.PostSession.BreaksTaken.Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownPreset_GivesABlankPresetName()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End));
        await SeedAsync(block);

        var result = await MarkAsync(block.Id);

        result.PostSession!.PresetName.Should().Be(string.Empty);
    }
}
