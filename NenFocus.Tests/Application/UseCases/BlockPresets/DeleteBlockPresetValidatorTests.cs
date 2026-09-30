using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class DeleteBlockPresetValidatorTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public DeleteBlockPresetValidatorTests()
    {
        _clock.SetNow(DateTime.UtcNow);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _plannedBlocks.HasFutureBlocksForPresetAsync(Arg.Any<Guid>(), Arg.Any<Moment>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private DeleteBlockPresetValidator CreateValidator() => new(_sessions, _plannedBlocks, _clock);

    [Fact]
    public async Task Validate_NotInUse_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(new DeleteBlockPresetCommand { PresetId = Guid.NewGuid() });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InUseByActiveSession_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId, SessionOrigin.Spontaneous);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(new DeleteBlockPresetCommand { PresetId = presetId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_HasFutureBlocks_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _plannedBlocks.HasFutureBlocksForPresetAsync(presetId, Arg.Any<Moment>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateValidator().ValidateAsync(new DeleteBlockPresetCommand { PresetId = presetId });

        result.IsValid.Should().BeFalse();
    }
}
