using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

// Placement itself (past, contract hours, free for Focus, the contract found through
// attribution) is GetBlockPlacementVerdictQuery's answer since Gap 88 phase 8 — tested in
// GetBlockPlacementVerdictQueryHandlerTests. Here: the form checks, and that the validator
// asks the query with the command's values and passes its refusal on.
public class CreatePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto> _placement =
        Substitute.For<IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto>>();

    public CreatePlannedBlockValidatorTests()
    {
        _placement.HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>())
            .Returns(new BlockPlacementVerdictDto { Outcome = PlacementOutcome.Allowed });
    }

    private CreatePlannedBlockValidator CreateValidator() => new(_presets, _placement);

    private static CreatePlannedBlockCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        BlockPresetType = BlockPresetType.Focus,
        Slot = At.Slot(Now.AddHours(1), Now.AddHours(2))
    };

    private Guid FocusPreset()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now)));
        return presetId;
    }

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(FocusPreset()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyPresetId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PresetTypeMismatch_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Now)));

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ShorterThanOneMinute_IsInvalid()
    {
        var command = ValidCommand(FocusPreset()) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddSeconds(30)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ExactlyOneMinute_IsValid()
    {
        var command = ValidCommand(FocusPreset()) with { Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddMinutes(1)) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_FocusBlock_ExceedsMaxDuration_IsInvalid()
    {
        var command = ValidCommand(FocusPreset()) with
        {
            Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddHours(FocusBlock.MaxDurationHours).AddMinutes(1))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_FocusBlock_AtExactlyMaxDuration_IsValid()
    {
        var command = ValidCommand(FocusPreset()) with
        {
            Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddHours(FocusBlock.MaxDurationHours))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BackgroundBlock_ExceedsFocusMaxDuration_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Now)));
        var command = ValidCommand(presetId) with
        {
            BlockPresetType = BlockPresetType.Background,
            Slot = At.Slot(Now.AddHours(1), Now.AddHours(1).AddHours(FocusBlock.MaxDurationHours).AddDays(1))
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PlacementRefused_IsInvalidWithTheQuerysMessage()
    {
        _placement.HandleAsync(Arg.Any<GetBlockPlacementVerdictQuery>(), Arg.Any<CancellationToken>())
            .Returns(new BlockPlacementVerdictDto { Outcome = PlacementOutcome.TimeTaken, Message = "Taken." });

        var result = await CreateValidator().ValidateAsync(ValidCommand(FocusPreset()));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Taken.");
    }

    [Fact]
    public async Task Validate_AsksThePlacementQueryWithTheCommandsValues()
    {
        var presetId = FocusPreset();
        var command = ValidCommand(presetId);

        await CreateValidator().ValidateAsync(command);

        await _placement.Received(1).HandleAsync(
            new GetBlockPlacementVerdictQuery(command.Slot, BlockPresetType.Focus, presetId),
            Arg.Any<CancellationToken>());
    }
}
