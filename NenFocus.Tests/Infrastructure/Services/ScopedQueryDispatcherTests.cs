using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure.Services;

namespace NenFocus.Tests.Infrastructure.Services;

public class ScopedQueryDispatcherTests
{
    private sealed record EchoQuery(string Value) : IQuery<string>;

    private sealed class EchoQueryHandler : IQueryHandler<EchoQuery, string>
    {
        public CancellationToken ReceivedToken { get; private set; }

        public Task<string> HandleAsync(EchoQuery query, CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            return Task.FromResult(query.Value);
        }
    }

    // Scoped so each fresh DI scope produces a distinct instance — the fact this test relies
    // on to prove the dispatcher actually creates a new scope per call, not just once.
    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed record MarkerQuery : IQuery<Guid>;

    private sealed class MarkerQueryHandler : IQueryHandler<MarkerQuery, Guid>
    {
        private readonly ScopeMarker _marker;
        public MarkerQueryHandler(ScopeMarker marker) => _marker = marker;
        public Task<Guid> HandleAsync(MarkerQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(_marker.Id);
    }

    private static ScopedQueryDispatcher CreateDispatcher(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return new ScopedQueryDispatcher(provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task QueryAsync_ResolvesRegisteredHandler_ReturnsItsResult()
    {
        var services = new ServiceCollection();
        services.AddTransient<IQueryHandler<EchoQuery, string>, EchoQueryHandler>();

        var result = await CreateDispatcher(services).QueryAsync(new EchoQuery("hello"));

        result.Should().Be("hello");
    }

    [Fact]
    public async Task QueryAsync_PassesTheCancellationTokenThroughToTheHandler()
    {
        var handler = new EchoQueryHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IQueryHandler<EchoQuery, string>>(handler);
        using var cts = new CancellationTokenSource();

        await CreateDispatcher(services).QueryAsync(new EchoQuery("hello"), cts.Token);

        handler.ReceivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task QueryAsync_NoHandlerRegistered_Throws()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);

        var act = async () => await dispatcher.QueryAsync(new EchoQuery("hello"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task QueryAsync_TwoCalls_EachResolveFromAnIndependentScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();
        services.AddTransient<IQueryHandler<MarkerQuery, Guid>, MarkerQueryHandler>();
        var dispatcher = CreateDispatcher(services);

        var first = await dispatcher.QueryAsync(new MarkerQuery());
        var second = await dispatcher.QueryAsync(new MarkerQuery());

        // Same Scoped-registered type, two different instances — proves each QueryAsync
        // call got its own DI scope rather than reusing one across calls, which is the
        // whole point: a real DbContext registered the same way would never be shared
        // across unrelated operations either.
        first.Should().NotBe(second);
    }
}
