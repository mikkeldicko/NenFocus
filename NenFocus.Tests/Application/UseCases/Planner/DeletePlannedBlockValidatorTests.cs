using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class DeletePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public DeletePlannedBlockValidatorTests()
    {
        _clock.UtcNow.Returns(Now);
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);
    }

    private DeletePlannedBlockValidator CreateValidator() => new(_blocks, _sessions, _contracts, _clock);

    private static NenContract ActiveContract() =>
        NenContract.Seal(
            Guid.NewGuid(), "Study Month", new NenContractGoal("Ship it"),
            startDate: DateOnly.FromDateTime(Now), endDate: DateOnly.FromDateTime(Now).AddDays(6),
            StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), sealedAt: Now); // sealedAt >= startDate -> Active

    private static NenContract CompletedContract()
    {
        var contract = NenContract.Seal(
            Guid.NewGuid(), "Study Month", new NenContractGoal("Ship it"),
            startDate: DateOnly.FromDateTime(Now).AddDays(-10), endDate: DateOnly.FromDateTime(Now).AddDays(-3),
            StrictnessRule.Locked, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), sealedAt: Now.AddDays(-10)); // Active on seal
        contract.Complete(Now.AddDays(-2));
        return contract;
    }

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
    public async Task Validate_FocusBlockAlreadyEnded_IsInvalid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BackgroundBlockAlreadyEnded_IsInvalid()
    {
        // Background Blocks have no Ritual/RitualCompletedAt at all — HasEnded is the only
        // signal that can protect their historical record, and it must apply here too.
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_MissedFocusBlock_IsInvalid()
    {
        // Ritual never completed, but the window has closed — this is a "missed" record
        // the domain explicitly says must not be deleted or altered.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-1));
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        block.RitualCompletedAt.Should().BeNull(); // confirms this is the "missed", not "completed", case
        result.IsValid.Should().BeFalse();
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
        // Mid-window, no active session tied to it — HasEnded is false, so this rule alone
        // does not block it (the active-session rule is a separate, independent check).
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
        var contract = ActiveContract();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: contract.Id);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BlockUnderCompletedContract_IsValid()
    {
        // Not in effect anymore — falls back to the ordinary HasEnded-based protection,
        // which this future-dated block passes.
        var contract = CompletedContract();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: contract.Id);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BlockWithContractIdButContractNoLongerResolvable_IsValid()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: Guid.NewGuid());
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);

        var result = await CreateValidator().ValidateAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

        result.IsValid.Should().BeTrue();
    }
}
