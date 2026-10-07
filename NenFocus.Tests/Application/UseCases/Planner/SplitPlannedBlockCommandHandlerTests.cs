using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.SplitPlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

// The block divides itself (FocusBlock.SplitInto, tested in FocusBlockTests); the handler replaces
// the original with its two pieces. A split never recalculates coverage (Gap 106, decision 2).
public class SplitPlannedBlockCommandHandlerTests
{
    private static readonly DateTime OriginalStart = new(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OriginalEnd = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();

    private SplitPlannedBlockCommandHandler CreateHandler() => new(_blocks);

    private static SplitPlannedBlockCommand ValidCommand(Guid blockId) => new()
    {
        OriginalBlockId = blockId,
        First = At.Slot(OriginalStart, OriginalStart.AddHours(2)),
        Second = At.Slot(OriginalStart.AddHours(2), OriginalEnd)
    };

    private async Task<(FocusBlock First, FocusBlock Second)> SavedPiecesAsync(SplitPlannedBlockCommand command)
    {
        var saved = new List<PlannedBlock>();
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(saved.Add), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        return ((FocusBlock)saved.Single(b => b.Id == result.FirstBlockId), (FocusBlock)saved.Single(b => b.Id == result.SecondBlockId));
    }

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
            .Returns(FocusBlock.Create(blockId, presetId, At.Slot(OriginalStart, OriginalEnd), "My Block", TestContracts.Committed(contractId)));

        var (first, second) = await SavedPiecesAsync(ValidCommand(blockId));

        await _blocks.Received(1).DeleteAsync(blockId, Arg.Any<CancellationToken>());
        await _blocks.Received(2).AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
        first.Id.Should().NotBe(second.Id);
        first.Scheduled.Should().Be(At.Slot(OriginalStart, OriginalStart.AddHours(2)));
        second.Scheduled.Should().Be(At.Slot(OriginalStart.AddHours(2), OriginalEnd));
        foreach (var piece in new[] { first, second })
        {
            piece.BlockPresetId.Should().Be(presetId);
            piece.Membership.Should().Be(TestContracts.Committed(contractId));
            piece.Title.Should().Be("My Block");
        }
    }

    [Fact]
    public async Task HandleAsync_RedemptionBlock_DividesItsReceipt_AndShortensNeitherPiece()
    {
        // A 3h block covering 90 min, cut at 2h: the first piece takes all 90 minutes, the second
        // none — and both keep the requested slots (Gap 95, decision 16).
        var blockId = Guid.NewGuid();
        var missedBlockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.CreateWithReceipt(blockId, Guid.NewGuid(), At.Slot(OriginalStart, OriginalEnd), "My Block",
                TestContracts.Redemption(Guid.NewGuid()), receipt: new RedemptionReceipt([new(missedBlockId, WholeMinutes.Of(90))])));

        var (first, second) = await SavedPiecesAsync(ValidCommand(blockId));

        first.Receipt.Entries.Should().Equal(new RedemptionEntry(missedBlockId, WholeMinutes.Of(90)));
        second.Receipt.Should().Be(RedemptionReceipt.None);
        first.Scheduled.Should().Be(At.Slot(OriginalStart, OriginalStart.AddHours(2)));
        second.Scheduled.Should().Be(At.Slot(OriginalStart.AddHours(2), OriginalEnd));
        first.IsRedemption.Should().BeTrue();
        second.IsRedemption.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NeitherNewBlockInheritsOriginalId()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), At.Slot(OriginalStart, OriginalEnd)));

        await CreateHandler().HandleAsync(ValidCommand(blockId));

        await _blocks.DidNotReceive().AddAsync(
            Arg.Is<PlannedBlock>(b => b.Id == blockId), Arg.Any<CancellationToken>());
    }
}
