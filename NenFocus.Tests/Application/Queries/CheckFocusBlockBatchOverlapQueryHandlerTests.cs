using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckFocusBlockBatchOverlap;
using NenFocus.Domain.Entities;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class CheckFocusBlockBatchOverlapQueryHandlerTests
{
    private static readonly Guid PresetId = Guid.NewGuid();

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();

    private CheckFocusBlockBatchOverlapQueryHandler CreateHandler() => new(_blocks);

    [Fact]
    public async Task HandleAsync_EmptyBatch_ReturnsFalse()
    {
        var result = await CreateHandler().HandleAsync(
            new CheckFocusBlockBatchOverlapQuery([]), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NoPersistedOverlap_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), PresetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0));
        _blocks.GetOverlappingAsync(block.Start, block.End, ritualOnly: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(
            new CheckFocusBlockBatchOverlapQuery([block]), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_OnePersistedOverlap_ReturnsTrue()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), PresetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0));
        var existing = FocusBlock.Create(Guid.NewGuid(), PresetId, block.Start, block.End);
        _blocks.GetOverlappingAsync(block.Start, block.End, ritualOnly: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckFocusBlockBatchOverlapQuery([block]), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SecondBlockOverlapsWhileFirstDoesNot_ReturnsTrue()
    {
        var first = FocusBlock.Create(Guid.NewGuid(), PresetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0));
        var second = FocusBlock.Create(Guid.NewGuid(), PresetId,
            new DateTime(2026, 1, 1, 12, 0, 0), new DateTime(2026, 1, 1, 13, 0, 0));
        _blocks.GetOverlappingAsync(first.Start, first.End, ritualOnly: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _blocks.GetOverlappingAsync(second.Start, second.End, ritualOnly: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), PresetId, second.Start, second.End)]);

        var result = await CreateHandler().HandleAsync(
            new CheckFocusBlockBatchOverlapQuery([first, second]), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_OverlapBelongsToExcludedContract_ReturnsFalse()
    {
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), PresetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0));
        var ownPriorBlock = FocusBlock.Create(Guid.NewGuid(), PresetId, block.Start, block.End, contractId: contractId);
        _blocks.GetOverlappingAsync(block.Start, block.End, ritualOnly: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[ownPriorBlock]);

        var result = await CreateHandler().HandleAsync(
            new CheckFocusBlockBatchOverlapQuery([block], ExcludeContractId: contractId), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ExcludeContractId_StillCatchesOverlapWithADifferentContract()
    {
        var editingContractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), PresetId,
            new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0));
        var someoneElsesBlock = FocusBlock.Create(Guid.NewGuid(), PresetId, block.Start, block.End, contractId: Guid.NewGuid());
        _blocks.GetOverlappingAsync(block.Start, block.End, ritualOnly: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[someoneElsesBlock]);

        var result = await CreateHandler().HandleAsync(
            new CheckFocusBlockBatchOverlapQuery([block], ExcludeContractId: editingContractId), CancellationToken.None);

        result.Should().BeTrue();
    }
}
