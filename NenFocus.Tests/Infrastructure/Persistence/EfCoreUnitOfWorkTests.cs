using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Persistence;

public class EfCoreUnitOfWorkTests : SqliteRepositoryTestBase
{
    private readonly IDomainEventPublisher _publisher = Substitute.For<IDomainEventPublisher>();

    private EfCoreUnitOfWork CreateUnitOfWork() => new(Context, _publisher);

    [Fact]
    public async Task CommitAsync_PersistsPendingChanges()
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow));
        await Context.BlockPresets.AddAsync(preset);

        await CreateUnitOfWork().CommitAsync();

        var reloaded = await Context.BlockPresets.FindAsync(preset.Id);
        reloaded.Should().NotBeNull();
    }

    [Fact]
    public async Task CommitAsync_PublishesDomainEventsForAggregatesWithPendingEvents()
    {
        // BlockPreset.Create() raises BlockPresetCreatedEvent internally. The publisher
        // receives a live ReadOnlyCollection view over the aggregate's internal event list,
        // so it must be snapshotted with .ToList() at call time — by the time a NSubstitute
        // assertion re-evaluates the call log, ClearDomainEvents() has already emptied it.
        List<DomainEvent>? capturedEvents = null;
        _publisher.PublishAllAsync(Arg.Any<IEnumerable<DomainEvent>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(ci => capturedEvents = ci.Arg<IEnumerable<DomainEvent>>().ToList());

        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow));
        await Context.BlockPresets.AddAsync(preset);

        await CreateUnitOfWork().CommitAsync();

        capturedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BlockPresetCreatedEvent>()
            .Which.PresetId.Should().Be(preset.Id);
    }

    [Fact]
    public async Task CommitAsync_ClearsDomainEventsAfterPublishing()
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow));
        await Context.BlockPresets.AddAsync(preset);

        await CreateUnitOfWork().CommitAsync();

        preset.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_AggregateWithNoPendingEvents_IsNotPublished()
    {
        // A freshly-created block holds no domain event (only its transitions — cancelled,
        // session ended, missed — raise one), so committing it publishes nothing.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(
            new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)));
        await Context.Blocks.AddAsync(block);

        await CreateUnitOfWork().CommitAsync();

        await _publisher.DidNotReceive().PublishAllAsync(Arg.Any<IEnumerable<DomainEvent>>(), Arg.Any<CancellationToken>());
    }
}
