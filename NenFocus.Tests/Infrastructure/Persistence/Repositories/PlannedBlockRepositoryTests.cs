using FluentAssertions;
using NenFocus.Domain.Entities;
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
    public async Task GetOverlappingAsync_RitualOnly_ExcludesBackgroundBlocks()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await repo.AddAsync(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(Start, End, ritualOnly: true);

        result.Should().ContainSingle().Which.Should().BeOfType<FocusBlock>();
    }

    [Fact]
    public async Task GetOverlappingAsync_ExcludeId_OmitsThatBlock()
    {
        var repo = CreateRepository();
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await repo.AddAsync(block);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetOverlappingAsync(Start, End, excludeId: block.Id);

        result.Should().BeEmpty();
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
    public async Task HasActiveFocusBlockAsync_WithinWindow_ReturnsTrue()
    {
        var repo = CreateRepository();
        await repo.AddAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasActiveFocusBlockAsync(Start.AddMinutes(30));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasActiveFocusBlockAsync_BackgroundBlockActiveOnly_ReturnsFalse()
    {
        var repo = CreateRepository();
        await repo.AddAsync(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasActiveFocusBlockAsync(Start.AddMinutes(30));

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

    [Fact]
    public async Task GetUnresolvedFocusBlocksAsync_BackgroundBlock_IsExcluded()
    {
        var repo = CreateRepository();
        await repo.AddAsync(BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End));
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetUnresolvedFocusBlocksAsync(End.AddMinutes(1));

        result.Should().BeEmpty();
    }
}
