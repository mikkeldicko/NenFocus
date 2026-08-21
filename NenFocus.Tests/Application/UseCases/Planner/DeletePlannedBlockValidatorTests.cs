using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class DeletePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public DeletePlannedBlockValidatorTests()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private DeletePlannedBlockValidator CreateValidator() => new(_blocks, _sessions);

    [Fact]
    public async Task Validate_ValidBlockId_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FocusBlockAlreadyEndedNotContractLinked_IsValid()
    {
        // Ended blocks are no longer permanently protected — only contract-linkage protects
        // a block from deletion now, so this one (no ContractId) is deletable from its
        // Planner snapshot view.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlockAlreadyEndedNotContractLinked_IsValid()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MissedFocusBlockNotContractLinked_IsValid()
    {
        // Ritual never completed, window closed — deletable from the snapshot view just
        // like a completed one, as long as it's not tied to a Nen Contract.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        block.RitualCompletedAt.Should().BeNull(); // confirms this is the "missed", not "completed", case
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_FutureBlock_IsValid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_CurrentlyActiveBlockNotYetEnded_IsValid()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1), Now.AddHours(1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ActiveSessionsOriginatingBlock_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1), Now.AddHours(1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(2)),
            block.BlockPresetId, NenFocus.Domain.Enums.SessionOrigin.Planned, originatingBlockId: block.Id);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ActiveSessionOriginatingFromADifferentBlock_DoesNotBlockThisOne()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
            Guid.NewGuid(), NenFocus.Domain.Enums.SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ActiveSpontaneousSession_DoesNotBlockAnyPlannedBlock()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
            Guid.NewGuid(), NenFocus.Domain.Enums.SessionOrigin.Spontaneous); // OriginatingBlockId null
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockUnderActiveContract_IsInvalid()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: contractId);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockUnderConcludedContract_IsStillInvalid()
    {
        // Contract-linkage protection is permanent — once a block's ContractId is set, it
        // stays undeletable forever, even after the contract itself has concluded, so a
        // contract's historical stats can never silently change.
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1), contractId: contractId);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }
}
