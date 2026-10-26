using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

public class PlannedBlockRepositoryTests : SqliteRepositoryTestBase
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private PlannedBlockRepository CreateRepository() => new(Context);

    /// <summary>Reads through a context of its own, so they see the database, not the tracker that saved (Gap 110).</summary>
    private PlannedBlockRepository Fresh() => new(CreateFreshContext());

    [Fact]
    public async Task Add_FocusBlock_ReloadsAsFocusBlockViaDiscriminator()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        repo.Add(block);
        await Context.SaveChangesAsync();

        var reloaded = await Fresh().GetByIdAsync(block.Id);
        reloaded.Should().BeOfType<FocusBlock>();
    }

    [Fact]
    public async Task Add_BackgroundBlock_ReloadsAsBackgroundBlockViaDiscriminator()
    {
        var repo = CreateRepository();
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        repo.Add(block);
        await Context.SaveChangesAsync();

        var reloaded = await Fresh().GetByIdAsync(block.Id);
        reloaded.Should().BeOfType<BackgroundBlock>();
    }

    [Fact]
    public async Task GetForRangeAsync_ReturnsOverlappingBlocksOrderedByStart()
    {
        var repo = CreateRepository();
        var later = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start.AddHours(2), End.AddHours(2)));
        var earlier = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        repo.Add(later);
        repo.Add(earlier);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetForRangeAsync(new TimeSlot(At.Utc(Start.AddMinutes(-30)), At.Utc(End.AddHours(3))));

        result.Select(b => b.Id).Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public async Task GetForRangeAsync_CancelledBlock_IsExcluded()
    {
        // A cancelled block
        // (its owning contract was broken) has vacated its schedule entirely and must
        // never count as a live block to any caller of this method either.
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkCancelled(At.Utc(Start.AddMinutes(-10)));
        repo.Add(block);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetForRangeAsync(new TimeSlot(At.Utc(Start.AddMinutes(-30)), At.Utc(End.AddHours(3))));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_ThenCommit_IsGone()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End));
        CreateRepository().Add(block);
        await Context.SaveChangesAsync();

        await using (var scope = CreateFreshContext())
        {
            var repository = new PlannedBlockRepository(scope);
            repository.Remove((await repository.GetByIdAsync(block.Id))!);
            await scope.SaveChangesAsync();
        }

        (await Fresh().GetByIdAsync(block.Id)).Should().BeNull();
    }

    // GetUsingPresetAsync narrows; each block decides through ReservedSlot (Gap 89 Slice D).

    [Fact]
    public async Task GetUsingPresetAsync_ABlockEndingAfterTheCutoff_IsReturned()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, At.Slot(Start, End));
        repo.Add(block);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetUsingPresetAsync(presetId, At.Utc(Start.AddMinutes(-1)));

        result.Select(b => b.Id).Should().Equal(block.Id);
    }

    [Fact]
    public async Task GetUsingPresetAsync_ABlockEndedByTheCutoff_IsLeftOut()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        repo.Add(FocusBlock.Create(Guid.NewGuid(), presetId, At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        (await Fresh().GetUsingPresetAsync(presetId, At.Utc(End))).Should().BeEmpty("half-open: ended at its end");
    }

    [Fact]
    public async Task GetUsingPresetAsync_ACancelledBlock_IsReturned_TheBlockDecides()
    {
        // A superset: what cancelling means is ReservedSlot's answer, not the SQL's.
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, At.Slot(Start, End));
        block.MarkCancelled(At.Utc(Start.AddMinutes(-10)));
        repo.Add(block);
        await Context.SaveChangesAsync();

        (await Fresh().GetUsingPresetAsync(presetId, At.Utc(Start.AddMinutes(-1)))).Select(b => b.Id).Should().Equal(block.Id);
    }

    [Fact]
    public async Task GetUsingPresetAsync_AnotherPresetsBlock_IsLeftOut()
    {
        var repo = CreateRepository();
        repo.Add(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        (await Fresh().GetUsingPresetAsync(Guid.NewGuid(), At.Utc(Start.AddMinutes(-1)))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_ClosedWindowRitualNeverCompleted_ReturnsBlock()
    {
        var repo = CreateRepository();
        repo.Add(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await Fresh().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_RitualCompleted_IsExcluded()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        repo.Add(block);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_WindowNotYetClosed_IsExcluded()
    {
        var repo = CreateRepository();
        repo.Add(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await Fresh().GetUnresolvedFocusBlocksAsync(At.Utc(Start.AddMinutes(30)));

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false, -30)]   // window still open
    [InlineData(false, false, 0)]     // exactly at End
    [InlineData(false, false, 1)]     // window closed, never started → unresolved
    [InlineData(true, false, 1)]      // Ritual completed
    [InlineData(false, true, 1)]      // cancelled
    [InlineData(true, true, 1)]
    public async Task GetUnresolvedFocusBlocksAsync_AgreesWithTheDomainRule(
        bool ritualCompleted, bool cancelled, int minutesAfterEnd)
    {
        // The SQL mirrors FocusBlock.IsUnresolved for speed; this keeps the two in step.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        if (ritualCompleted) block.MarkRitualCompleted(At.Utc(Start));
        if (cancelled) block.MarkCancelled(At.Utc(Start.AddMinutes(-10)));
        CreateRepository().Add(block);
        await Context.SaveChangesAsync();
        var asOf = End.AddMinutes(minutesAfterEnd);

        var result = await Fresh().GetUnresolvedFocusBlocksAsync(At.Utc(asOf));

        result.Any(b => b.Id == block.Id).Should().Be(block.IsUnresolved(At.Utc(asOf)));
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_BackgroundBlock_IsExcluded()
    {
        var repo = CreateRepository();
        repo.Add(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await Fresh().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_CancelledBlock_IsExcluded()
    {
        // A block cancelled by breaking its contract never ran its Ritual by design —
        // it must never surface as Missed once its original window passes.
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkCancelled(At.Utc(Start.AddMinutes(-10)));
        repo.Add(block);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_NoRedemptionEntries_ReloadsAsEmpty()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        repo.Add(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await Fresh().GetByIdAsync(block.Id))!;
        reloaded.Receipt.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_WithRedemptionEntries_RoundTripsThroughJsonColumn()
    {
        var repo = CreateRepository();
        var missedBlockId = Guid.NewGuid();
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End),
            title: null, membership: TestContracts.Redemption(Guid.NewGuid()), receipt: new RedemptionReceipt([new RedemptionEntry(missedBlockId, WholeMinutes.Of(30))]));

        repo.Add(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await Fresh().GetByIdAsync(block.Id))!;
        reloaded.Receipt.Entries.Should().ContainSingle(e =>
            e.MissedBlockId == missedBlockId && e.Covered == WholeMinutes.Of(30));
    }

    [Fact]
    public async Task Add_WithContractId_RoundTrips()
    {
        var repo = CreateRepository();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), membership: TestContracts.Committed(contractId));

        repo.Add(block);
        await Context.SaveChangesAsync();

        var reloaded = await Fresh().GetByIdAsync(block.Id);
        reloaded!.Membership.Should().Be(TestContracts.Committed(contractId));
    }
}
