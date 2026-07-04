using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class UpdatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public UpdatePlannedBlockCommandHandlerTests()
    {
        _blocks.GetOverlappingAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private UpdatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _profiles, _sessions);

    private static UpdatePlannedBlockCommand ValidCommand(Guid blockId, Guid profileId) => new()
    {
        BlockId = blockId,
        ProfileId = profileId,
        Start = Start,
        End = End
    };

    private static BlockProfile FocusProfile(Guid id)
        => BlockProfile.Create(id, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_BlockNotFound_Fails()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(Guid.NewGuid(), Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ProfileNotFound_Fails()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(), Start, End));
        _profiles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_OverlapsAnotherFocusBlock_Fails()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Start, End));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(FocusProfile(profileId));
        _blocks.GetOverlappingAsync(Start, End, true, blockId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), profileId, Start, End)]);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, profileId));

        result.Success.Should().BeFalse();
        await _blocks.DidNotReceive().UpdateAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_OverlapsActiveSession_Fails()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Start, End));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(FocusProfile(profileId));
        var activeSession = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(2)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        activeSession.Start(Start.AddMinutes(-30));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(activeSession);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, profileId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already an active focus session");
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_SkipsOverlapAndSessionChecks()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, profileId, Start, End));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Background", WebsitePolicy.Blocklist, BlockType.Background, DateTime.UtcNow));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, profileId));

        result.Success.Should().BeTrue();
        await _blocks.DidNotReceive().GetOverlappingAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReschedulesAndReassignsProfile()
    {
        var blockId = Guid.NewGuid();
        var oldProfileId = Guid.NewGuid();
        var newProfileId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, oldProfileId, Start.AddDays(-1), End.AddDays(-1));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _profiles.GetByIdAsync(newProfileId, Arg.Any<CancellationToken>()).Returns(FocusProfile(newProfileId));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, newProfileId));

        result.Success.Should().BeTrue();
        block.Start.Should().Be(Start);
        block.End.Should().Be(End);
        block.BlockProfileId.Should().Be(newProfileId);
        await _blocks.Received(1).UpdateAsync(block, Arg.Any<CancellationToken>());
    }
}
