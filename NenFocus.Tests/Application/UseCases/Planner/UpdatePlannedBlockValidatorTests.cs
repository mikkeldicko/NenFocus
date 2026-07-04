using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class UpdatePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public UpdatePlannedBlockValidatorTests() => _clock.UtcNow.Returns(Now);

    private UpdatePlannedBlockValidator CreateValidator() => new(_blocks, _profiles, _clock);

    private static UpdatePlannedBlockCommand ValidCommand(Guid blockId, Guid profileId) => new()
    {
        BlockId = blockId,
        ProfileId = profileId,
        Start = Now.AddHours(1),
        End = Now.AddHours(2)
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Now.AddHours(1), Now.AddHours(2)));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, profileId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyBlockId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyProfileId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid(), Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ProfileTypeDoesNotMatchBlockType_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Now.AddHours(1), Now.AddHours(2)));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Background", WebsitePolicy.Blocklist, BlockType.Background, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(blockId, profileId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_StartInPast_ButBlockAlreadyStarted_SkipsFutureCheck()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        // Existing block's own Start is already in the past (it's live/locked).
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Now.AddMinutes(-30), Now.AddHours(1)));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        // The UI sends back the original (now-past) start time unchanged.
        var command = ValidCommand(blockId, profileId) with { Start = Now.AddMinutes(-30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_StartInPast_BlockNotYetStarted_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Now.AddHours(1), Now.AddHours(2)));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        var command = ValidCommand(blockId, profileId) with { Start = Now.AddMinutes(-10) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EndBeforeStart_IsInvalid()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Now.AddHours(1), Now.AddHours(2)));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        var command = ValidCommand(blockId, profileId) with { End = Now.AddMinutes(30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
