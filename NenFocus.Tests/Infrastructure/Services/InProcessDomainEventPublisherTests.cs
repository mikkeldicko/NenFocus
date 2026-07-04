using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Events;
using NenFocus.Infrastructure.Services;

namespace NenFocus.Tests.Infrastructure.Services;

public class InProcessDomainEventPublisherTests
{
    private sealed class RecordingHandler<TEvent> : IDomainEventHandler<TEvent> where TEvent : DomainEvent
    {
        public List<TEvent> Received { get; } = [];

        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private static InProcessDomainEventPublisher CreatePublisher(IServiceCollection services)
        => new(services.BuildServiceProvider());

    [Fact]
    public async Task PublishAsync_ResolvesHandlerForEventType_AndInvokesIt()
    {
        var handler = new RecordingHandler<SessionStartedEvent>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<SessionStartedEvent>>(handler);

        var domainEvent = new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow);
        await CreatePublisher(services).PublishAsync(domainEvent);

        handler.Received.Should().ContainSingle().Which.Should().BeSameAs(domainEvent);
    }

    [Fact]
    public async Task PublishAsync_NoHandlerRegisteredForEventType_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = async () => await CreatePublisher(services).PublishAsync(
            new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAllAsync_DispatchesEachEventToItsOwnHandlerType()
    {
        var startedHandler = new RecordingHandler<SessionStartedEvent>();
        var endedHandler = new RecordingHandler<SessionEndedEvent>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<SessionStartedEvent>>(startedHandler);
        services.AddSingleton<IDomainEventHandler<SessionEndedEvent>>(endedHandler);

        var started = new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow);
        var ended = new SessionEndedEvent(Guid.NewGuid(), DateTime.UtcNow);

        await CreatePublisher(services).PublishAllAsync([started, ended]);

        startedHandler.Received.Should().ContainSingle().Which.Should().BeSameAs(started);
        endedHandler.Received.Should().ContainSingle().Which.Should().BeSameAs(ended);
    }

    [Fact]
    public async Task PublishAllAsync_CancellationAlreadyRequested_ThrowsAndDispatchesNothing()
    {
        var handler = new RecordingHandler<SessionStartedEvent>();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<SessionStartedEvent>>(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await CreatePublisher(services).PublishAllAsync(
            [new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow)], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Received.Should().BeEmpty();
    }
}
