using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class SplitPlannedBlockCommandHandlerTests
{
    private static readonly DateTime OriginalStart = new(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OriginalEnd = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();

    private SplitPlannedBlockCommandHandler CreateHandler() => new(_blocks);

    private static SplitPlannedBlockCommand ValidCommand(Guid blockId) => new()
    {
        OriginalBlockId = blockId,
        FirstBlockStart = OriginalStart,
        FirstBlockEnd = OriginalStart.AddHours(2),
        SecondBlockStart = OriginalStart.AddHours(2),
        SecondBlockEnd = OriginalEnd
    };

    [Fact]
    public async Task HandleAsync_BlockNotFound_Fails()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_DeletesOriginalAndCreatesTwoNewBlocks()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var redemptionEntries = new List<RedemptionEntry> { new(Guid.NewGuid(), 15) };
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, OriginalStart, OriginalEnd, "My Block", contractId, redemptionEntries));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId));

        result.Success.Should().BeTrue();
        result.FirstBlockId.Should().NotBeNull();
        result.SecondBlockId.Should().NotBeNull();
        result.FirstBlockId!.Value.Should().NotBe(result.SecondBlockId!.Value);
        result.FirstBlockId.Value.Should().NotBe(blockId);
        result.SecondBlockId.Value.Should().NotBe(blockId);

        await _blocks.Received(1).DeleteAsync(blockId, Arg.Any<CancellationToken>());
        await _blocks.Received(2).AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());

        await _blocks.Received(1).AddAsync(
            Arg.Is<PlannedBlock>(b =>
                b is FocusBlock
                && b.Id == result.FirstBlockId
                && b.BlockPresetId == presetId
                && b.ContractId == contractId
                && b.Start == OriginalStart
                && b.End == OriginalStart.AddHours(2)
                && b.Title == "My Block"
                && ((FocusBlock)b).RedemptionEntries.Count == 1),
            Arg.Any<CancellationToken>());

        await _blocks.Received(1).AddAsync(
            Arg.Is<PlannedBlock>(b =>
                b is FocusBlock
                && b.Id == result.SecondBlockId
                && b.BlockPresetId == presetId
                && b.ContractId == contractId
                && b.Start == OriginalStart.AddHours(2)
                && b.End == OriginalEnd),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_CreatesTwoBackgroundBlocks()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, presetId, OriginalStart, OriginalEnd, contractId: contractId));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId));

        result.Success.Should().BeTrue();
        await _blocks.Received(2).AddAsync(Arg.Is<PlannedBlock>(b => b is BackgroundBlock), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NeitherNewBlockInheritsOriginalId()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, OriginalStart, OriginalEnd));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId));

        await _blocks.DidNotReceive().AddAsync(
            Arg.Is<PlannedBlock>(b => b.Id == blockId), Arg.Any<CancellationToken>());
    }
}
