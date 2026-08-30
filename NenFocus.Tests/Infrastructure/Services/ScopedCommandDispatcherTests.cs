using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure.Services;

namespace NenFocus.Tests.Infrastructure.Services;

public class ScopedCommandDispatcherTests
{
    private sealed record EchoCommand(string Value) : ICommand<string>;

    private sealed class EchoCommandHandler : ICommandHandler<EchoCommand, string>
    {
        public CancellationToken ReceivedToken { get; private set; }

        public Task<string> HandleAsync(EchoCommand command, CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            return Task.FromResult(command.Value);
        }
    }

    // Scoped so each fresh DI scope produces a distinct instance — the fact this test relies
    // on to prove the dispatcher actually creates a new scope per call, not just once.
    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed record MarkerCommand : ICommand<Guid>;

    private sealed class MarkerCommandHandler : ICommandHandler<MarkerCommand, Guid>
    {
        private readonly ScopeMarker _marker;
        public MarkerCommandHandler(ScopeMarker marker) => _marker = marker;
        public Task<Guid> HandleAsync(MarkerCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(_marker.Id);
    }

    private static ScopedCommandDispatcher CreateDispatcher(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return new ScopedCommandDispatcher(provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task DispatchAsync_ResolvesRegisteredHandler_ReturnsItsResult()
    {
        var services = new ServiceCollection();
        services.AddTransient<ICommandHandler<EchoCommand, string>, EchoCommandHandler>();

        var result = await CreateDispatcher(services).DispatchAsync(new EchoCommand("hello"));

        result.Should().Be("hello");
    }

    [Fact]
    public async Task DispatchAsync_PassesTheCancellationTokenThroughToTheHandler()
    {
        var handler = new EchoCommandHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<EchoCommand, string>>(handler);
        using var cts = new CancellationTokenSource();

        await CreateDispatcher(services).DispatchAsync(new EchoCommand("hello"), cts.Token);

        handler.ReceivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_Throws()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);

        var act = async () => await dispatcher.DispatchAsync(new EchoCommand("hello"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DispatchAsync_TwoCalls_EachResolveFromAnIndependentScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddTransient<ICommandHandler<MarkerCommand, Guid>, MarkerCommandHandler>();
        var dispatcher = CreateDispatcher(services);

        var first = await dispatcher.DispatchAsync(new MarkerCommand());
        var second = await dispatcher.DispatchAsync(new MarkerCommand());

        first.Should().NotBe(second);
    }
}
