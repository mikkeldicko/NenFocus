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
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded.Should().BeOfType<FocusBlock>();
    }

    [Fact]
    public async Task AddAsync_BackgroundBlock_ReloadsAsBackgroundBlockViaDiscriminator()
    {
        var repo = CreateRepository();
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded.Should().BeOfType<BackgroundBlock>();
    }

    [Fact]
    public async Task GetForDayAsync_ReturnsBlocksOverlappingThatDay()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start.AddDays(5), End.AddDays(5)));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetForDayAsync(DateOnly.FromDateTime(Start));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetForDayAsync_CrossMidnightBlock_FoundOnBothLocalDaysItOccupies()
    {
        // Built from LOCAL wall-clock time near midnight, not a fixed UTC instant, so this
        // actually exercises the local-day-boundary conversion GetForDayAsync must do
        // (date is a local calendar day, b.Start/End are UTC) rather than a case a naive
        // UTC-anchored boundary would happen to get right anyway. Whether this reproduces
        // the original bug on any given test machine depends on its local UTC offset (the
        // repository has no injectable clock to control it directly), but it always
        // verifies the fix's actual correctness regardless of host timezone.
        var localDay = DateTime.Today;
        var localStart = DateTime.SpecifyKind(localDay.AddHours(23), DateTimeKind.Local).ToUniversalTime();
        var localEnd = DateTime.SpecifyKind(localDay.AddHours(24.5), DateTimeKind.Local).ToUniversalTime();

        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), localStart, localEnd));
        await Context.SaveChangesAsync();

        var dayOneResult = await CreateRepository().GetForDayAsync(DateOnly.FromDateTime(localDay));
        var dayTwoResult = await CreateRepository().GetForDayAsync(DateOnly.FromDateTime(localDay.AddDays(1)));

        dayOneResult.Should().ContainSingle();
        dayTwoResult.Should().ContainSingle();
    }

    [Fact]
    public async Task GetForRangeAsync_ReturnsOverlappingBlocksOrderedByStart()
    {
        var repo = CreateRepository();
        var later = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start.AddHours(2), End.AddHours(2));
        var earlier = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await repo.AddAsync(later);
        await repo.AddAsync(earlier);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetForRangeAsync(Start.AddMinutes(-30), End.AddHours(3));

        result.Select(b => b.Id).Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public async Task GetForRangeAsync_CancelledBlock_IsExcluded()
    {
        // Mirrors GetOverlappingAsync_CancelledBlock_IsExcluded — a cancelled block
        // (its owning contract was broken) has vacated its schedule entirely and must
        // never count as a live block to any caller of this method either.
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkCancelled(Start.AddMinutes(-10));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetForRangeAsync(Start.AddMinutes(-30), End.AddHours(3));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_RitualOnly_ExcludesBackgroundBlocks()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await repo.AddAsync(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(Start, End, Start, ritualOnly: true);

        result.Should().ContainSingle().Which.Should().BeOfType<FocusBlock>();
    }

    [Fact]
    public async Task GetOverlappingAsync_ExcludeId_OmitsThatBlock()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(Start, End, Start, excludeId: block.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_CancelledBlock_IsExcluded()
    {
        // A cancelled block (its owning contract was broken) has vacated its schedule
        // entirely and must never block new/moved scheduling into its old time slot.
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkCancelled(Start.AddMinutes(-10));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(Start, End, Start);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_SessionEndedEarly_FreedTailIsNotReported()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkRitualCompleted(Start);
        var endedEarly = Start.AddMinutes(20);
        block.MarkSessionEnded(endedEarly);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(
            endedEarly.AddMinutes(5), End, asOf: endedEarly.AddMinutes(5), ritualOnly: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_SessionEndedEarly_UsedPortionStillReported()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkRitualCompleted(Start);
        var endedEarly = Start.AddMinutes(20);
        block.MarkSessionEnded(endedEarly);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(
            Start, endedEarly.AddMinutes(-5), asOf: endedEarly.AddMinutes(5), ritualOnly: true);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetOverlappingAsync_FullyMissedAndWindowClosed_WholeWindowIsFreed()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(
            Start, End, asOf: End.AddMinutes(1), ritualOnly: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_NotYetMissed_StillPending_StillReported()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(
            Start, End, asOf: Start.AddMinutes(10), ritualOnly: true);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateAsync_PersistsRescheduledWindow()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        block.Reschedule(Start.AddDays(1), End.AddDays(1));
        await repo.UpdateAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded!.Start.Should().Be(Start.AddDays(1));
        reloaded.End.Should().Be(End.AddDays(1));
    }

    [Fact]
    public async Task SaveAsync_NewBlock_Inserts()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);

        await repo.SaveAsync(block);
        await Context.SaveChangesAsync();

        (await CreateRepository().GetByIdAsync(block.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_ExistingBlock_Updates()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        block.MarkRitualCompleted(Start);
        await CreateRepository().SaveAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await CreateRepository().GetByIdAsync(block.Id))!;
        reloaded.RitualCompletedAt.Should().Be(Start);
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
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), presetId, Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasFutureBlocksForPresetAsync(presetId, Start.AddMinutes(-1));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasFutureBlocksForPresetAsync_BlockEndingBeforeCutoff_ReturnsFalse()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), presetId, Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasFutureBlocksForPresetAsync(presetId, End.AddMinutes(1));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasFutureBlocksForPresetAsync_CancelledBlock_IsExcluded()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, Start, End);
        block.MarkCancelled(Start.AddMinutes(-10));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasFutureBlocksForPresetAsync(presetId, Start.AddMinutes(-1));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_ClosedWindowRitualNeverCompleted_ReturnsBlock()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(End.AddMinutes(1));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_RitualCompleted_IsExcluded()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkRitualCompleted(Start);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(End.AddMinutes(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_WindowNotYetClosed_IsExcluded()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(Start.AddMinutes(30));

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
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        if (ritualCompleted) block.MarkRitualCompleted(Start);
        if (cancelled) block.MarkCancelled(Start.AddMinutes(-10));
        await CreateRepository().AddAsync(block);
        await Context.SaveChangesAsync();
        var asOf = End.AddMinutes(minutesAfterEnd);

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(asOf);

        result.Any(b => b.Id == block.Id).Should().Be(block.IsUnresolved(asOf));
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_BackgroundBlock_IsExcluded()
    {
        var repo = CreateRepository();
        await repo.AddAsync(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(End.AddMinutes(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_CancelledBlock_IsExcluded()
    {
        // A block cancelled by breaking its contract never ran its Ritual by design —
        // it must never surface as Missed once its original window passes.
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkCancelled(Start.AddMinutes(-10));
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(End.AddMinutes(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_NoRedemptionEntries_ReloadsAsEmpty()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await CreateRepository().GetByIdAsync(block.Id))!;
        reloaded.RedemptionEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_WithRedemptionEntries_RoundTripsThroughJsonColumn()
    {
        var repo = CreateRepository();
        var missedBlockId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End,
            redemptionEntries: [new RedemptionEntry(missedBlockId, 30)]);

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = (FocusBlock)(await CreateRepository().GetByIdAsync(block.Id))!;
        reloaded.RedemptionEntries.Should().ContainSingle(e =>
            e.MissedBlockId == missedBlockId && e.MinutesCovered == 30);
    }

    [Fact]
    public async Task AddAsync_WithContractId_RoundTrips()
    {
        var repo = CreateRepository();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End, contractId: contractId);

        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(block.Id);
        reloaded!.ContractId.Should().Be(contractId);
    }
}
