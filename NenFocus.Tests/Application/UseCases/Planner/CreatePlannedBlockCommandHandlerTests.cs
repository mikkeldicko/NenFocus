using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class CreatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public CreatePlannedBlockCommandHandlerTests()
    {
        _blocks.GetOverlappingAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private CreatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _profiles, _sessions);

    private static CreatePlannedBlockCommand FocusCommand(Guid profileId) => new()
    {
        ProfileId = profileId,
        BlockType = BlockType.Focus,
        Start = Start,
        End = End
    };

    [Fact]
    public async Task HandleAsync_ProfileNotFound_Fails()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(FocusCommand(profileId));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Focus_OverlappingFocusBlockExists_Fails()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        _blocks.GetOverlappingAsync(Start.ToUniversalTime(), End.ToUniversalTime(), true, null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), profileId, Start, End)]);

        var result = await CreateHandler().HandleAsync(FocusCommand(profileId));

        result.Success.Should().BeFalse();
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Focus_OverlapsActiveSession_Fails()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        var activeSession = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(2)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        activeSession.Start(Start.AddMinutes(-30));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(activeSession);

        var result = await CreateHandler().HandleAsync(FocusCommand(profileId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already an active focus session");
    }

    [Fact]
    public async Task HandleAsync_Focus_HappyPath_CreatesFocusBlock()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));

        var result = await CreateHandler().HandleAsync(FocusCommand(profileId));

        result.Success.Should().BeTrue();
        result.BlockId.Should().NotBeNull();
        await _blocks.Received(1).AddAsync(Arg.Is<PlannedBlock>(b => b is FocusBlock), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Background_SkipsOverlapAndActiveSessionChecks()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Background", WebsitePolicy.Blocklist, BlockType.Background, DateTime.UtcNow));
        var command = FocusCommand(profileId) with { BlockType = BlockType.Background };

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        await _blocks.Received(1).AddAsync(Arg.Is<PlannedBlock>(b => b is BackgroundBlock), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().GetOverlappingAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
