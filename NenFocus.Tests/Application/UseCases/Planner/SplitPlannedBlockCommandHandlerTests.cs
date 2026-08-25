using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
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
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();

    public SplitPlannedBlockCommandHandlerTests()
    {
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedemptionAttributionResult(((ComputeRedemptionAttributionQuery)ci[0]).ContractId, []));
    }

    private SplitPlannedBlockCommandHandler CreateHandler() => new(_blocks, _computeRedemptionAttribution);

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
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, OriginalStart, OriginalEnd, "My Block", contractId));

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
                && b.Title == "My Block"),
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
    public async Task HandleAsync_FocusBlock_QueriesAttributionOnceForTheCombinedSpan()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, OriginalStart, OriginalEnd, "My Block", contractId));

        await CreateHandler().HandleAsync(ValidCommand(blockId));

        await _computeRedemptionAttribution.Received(1).HandleAsync(
            Arg.Is<ComputeRedemptionAttributionQuery>(q =>
                q.ContractId == contractId && q.PresetId == presetId
                && q.BlockStart == OriginalStart && q.BlockEnd == OriginalEnd
                && q.ExcludeBlockId == blockId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_EntryEntirelyWithinFirstHalf_GoesOnlyToFirstHalf()
    {
        // First half is [OriginalStart, OriginalStart+2h) — a 15-minute entry fits
        // entirely inside it, so it must not also appear on the second half (the exact
        // double-count bug this design fixes).
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var missedBlockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, OriginalStart, OriginalEnd, "My Block", contractId));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new RedemptionEntryDto(missedBlockId, 15)]));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId));

        await _blocks.Received(1).AddAsync(
            Arg.Is<PlannedBlock>(b => b.Id == result.FirstBlockId
                && ((FocusBlock)b).RedemptionEntries.Count == 1
                && ((FocusBlock)b).RedemptionEntries[0].MinutesCovered == 15),
            Arg.Any<CancellationToken>());
        await _blocks.Received(1).AddAsync(
            Arg.Is<PlannedBlock>(b => b.Id == result.SecondBlockId
                && ((FocusBlock)b).RedemptionEntries.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_EntryStraddlingTheBoundary_SplitsAcrossBothHalves()
    {
        // First half is 2h (120 min) long. A 150-minute entry exceeds that, so 120
        // minutes should land on the first half and the remaining 30 on the second.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var missedBlockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, OriginalStart, OriginalEnd, "My Block", contractId));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new RedemptionEntryDto(missedBlockId, 150)]));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId));

        await _blocks.Received(1).AddAsync(
            Arg.Is<PlannedBlock>(b => b.Id == result.FirstBlockId
                && ((FocusBlock)b).RedemptionEntries.Count == 1
                && ((FocusBlock)b).RedemptionEntries[0].MissedBlockId == missedBlockId
                && ((FocusBlock)b).RedemptionEntries[0].MinutesCovered == 120),
            Arg.Any<CancellationToken>());
        await _blocks.Received(1).AddAsync(
            Arg.Is<PlannedBlock>(b => b.Id == result.SecondBlockId
                && ((FocusBlock)b).RedemptionEntries.Count == 1
                && ((FocusBlock)b).RedemptionEntries[0].MissedBlockId == missedBlockId
                && ((FocusBlock)b).RedemptionEntries[0].MinutesCovered == 30),
            Arg.Any<CancellationToken>());
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
