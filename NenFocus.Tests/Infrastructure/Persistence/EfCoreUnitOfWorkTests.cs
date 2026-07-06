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
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
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

        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        await Context.BlockPresets.AddAsync(preset);

        await CreateUnitOfWork().CommitAsync();

        capturedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BlockPresetCreatedEvent>()
            .Which.PresetId.Should().Be(preset.Id);
    }

    [Fact]
    public async Task CommitAsync_ClearsDomainEventsAfterPublishing()
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        await Context.BlockPresets.AddAsync(preset);

        await CreateUnitOfWork().CommitAsync();

        preset.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_AggregateWithNoPendingEvents_IsNotPublished()
    {
        // Session.Create() does not raise any domain event by itself (only Start/
        // StartBreak/Complete do), so a freshly-created, not-yet-started session
        // has zero pending events at commit time.
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        await Context.Sessions.AddAsync(session);

        await CreateUnitOfWork().CommitAsync();

        await _publisher.DidNotReceive().PublishAllAsync(Arg.Any<IEnumerable<DomainEvent>>(), Arg.Any<CancellationToken>());
    }
}
