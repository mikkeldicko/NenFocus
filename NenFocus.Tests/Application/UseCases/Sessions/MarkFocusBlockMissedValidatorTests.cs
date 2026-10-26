using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;
using NenFocus.Domain.Entities;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class MarkFocusBlockMissedValidatorTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public MarkFocusBlockMissedValidatorTests()
    {
        _clock.SetNow(End.AddMinutes(1)); // the window has just closed
    }

    private MarkFocusBlockMissedValidator CreateValidator() => new(_blocks, _sessions, _clock);

    [Fact]
    public async Task Validate_EmptyFocusBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(
            new MarkFocusBlockMissedCommand { FocusBlockId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockNotFound_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((Block?)null);
        _sessions.GetByOriginatingBlockIdAsync(blockId, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateValidator().ValidateAsync(
            new MarkFocusBlockMissedCommand { FocusBlockId = blockId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockIsBackgroundBlock_IsInvalid()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateValidator().ValidateAsync(
            new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_RitualAlreadyCompleted_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateValidator().ValidateAsync(
            new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_WindowStillOpen_IsInvalid()
    {
        // Gap 95: the validator asks the sweep's own rule, FocusBlock.IsUnresolved — a block can't be
        // missed before its window has closed.
        _clock.SetNow(End.AddMinutes(-1));
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateValidator().ValidateAsync(new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CancelledBlock_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End));
        block.MarkCancelled(At.Utc(Start.AddMinutes(10)));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateValidator().ValidateAsync(new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SessionAlreadyExistsForBlock_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(TestSessions.For(block.Id));

        var result = await CreateValidator().ValidateAsync(
            new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateValidator().ValidateAsync(
            new MarkFocusBlockMissedCommand { FocusBlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }
}
