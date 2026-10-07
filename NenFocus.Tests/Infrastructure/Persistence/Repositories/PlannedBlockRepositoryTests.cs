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

    [Fact]
    public async Task AddAsync_FocusBlock_ReloadsAsFocusBlockViaDiscriminator()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded.Should().BeOfType<FocusBlock>();
    }

    [Fact]
    public async Task AddAsync_BackgroundBlock_ReloadsAsBackgroundBlockViaDiscriminator()
    {
        var repo = CreateRepository();
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded.Should().BeOfType<BackgroundBlock>();
    }

    [Fact]
    public async Task GetForRangeAsync_ReturnsOverlappingBlocksOrderedByStart()
    {
        var repo = CreateRepository();
        var later = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start.AddHours(2), End.AddHours(2)));
        var earlier = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        await repo.AddAsync(later);
        await repo.AddAsync(earlier);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetForRangeAsync(new TimeSlot(At.Utc(Start.AddMinutes(-30)), At.Utc(End.AddHours(3))));

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
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetForRangeAsync(new TimeSlot(At.Utc(Start.AddMinutes(-30)), At.Utc(End.AddHours(3))));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_PersistsRescheduledWindow()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        block.Reschedule(At.Slot(Start.AddDays(1), End.AddDays(1)));
        await repo.UpdateAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded!.Scheduled.Should().Be(At.Slot(Start.AddDays(1), End.AddDays(1)));
    }

    [Fact]
    public async Task SaveAsync_NewBlock_Inserts()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        await repo.SaveAsync(block);
        await Context.SaveChangesAsync();

        (await CreateRepository().GetByIdAsync(block.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_ExistingBlock_Updates()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        block.MarkRitualCompleted(At.Utc(Start.AddMinutes(10)));
        block.MarkSessionEnded(At.Utc(Start.AddMinutes(20)));
        await CreateRepository().SaveAsync(block);
        await Context.SaveChangesAsync();

        // Both Domain-only stamps survive the round trip, mapped by name (Gap 95).
        var reloaded = (FocusBlock)(await CreateRepository().GetByIdAsync(block.Id))!;
        reloaded.WorkSlot().Should().Be(At.Slot(Start.AddMinutes(10), Start.AddMinutes(20)));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        var act = async () =>
        {
            await CreateRepository().DeleteAsync(Guid.NewGuid());
            await Context.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HasFutureBlocksForPresetAsync_BlockEndingAfterCutoff_ReturnsTrue()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasFutureBlocksForPresetAsync(presetId, At.Utc(Start.AddMinutes(-1)));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasFutureBlocksForPresetAsync_BlockEndingBeforeCutoff_ReturnsFalse()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasFutureBlocksForPresetAsync(presetId, At.Utc(End.AddMinutes(1)));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasFutureBlocksForPresetAsync_CancelledBlock_IsExcluded()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId,At.Slot(Start, End));
        block.MarkCancelled(At.Utc(Start.AddMinutes(-10)));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasFutureBlocksForPresetAsync(presetId, At.Utc(Start.AddMinutes(-1)));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_ClosedWindowRitualNeverCompleted_ReturnsBlock()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_RitualCompleted_IsExcluded()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));
        block.MarkRitualCompleted(At.Utc(Start));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_WindowNotYetClosed_IsExcluded()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(At.Utc(Start.AddMinutes(30)));

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
        await CreateRepository().AddAsync(block);
        await Context.SaveChangesAsync();
        var asOf = End.AddMinutes(minutesAfterEnd);

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(At.Utc(asOf));

        result.Any(b => b.Id == block.Id).Should().Be(block.IsUnresolved(At.Utc(asOf)));
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_BackgroundBlock_IsExcluded()
    {
        var repo = CreateRepository();
        await repo.AddAsync(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End)));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

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
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(At.Utc(End.AddMinutes(1)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_NoRedemptionEntries_ReloadsAsEmpty()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End));

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await CreateRepository().GetByIdAsync(block.Id))!;
        reloaded.Receipt.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_WithRedemptionEntries_RoundTripsThroughJsonColumn()
    {
        var repo = CreateRepository();
        var missedBlockId = Guid.NewGuid();
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End),
            title: null, membership: TestContracts.Redemption(Guid.NewGuid()), receipt: new RedemptionReceipt([new RedemptionEntry(missedBlockId, 30)]));

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await CreateRepository().GetByIdAsync(block.Id))!;
        reloaded.Receipt.Entries.Should().ContainSingle(e =>
            e.MissedBlockId == missedBlockId && e.MinutesCovered == 30);
    }

    [Fact]
    public async Task AddAsync_WithContractId_RoundTrips()
    {
        var repo = CreateRepository();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, End), membership: TestContracts.Committed(contractId));

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded!.Membership.Should().Be(TestContracts.Committed(contractId));
    }
}
