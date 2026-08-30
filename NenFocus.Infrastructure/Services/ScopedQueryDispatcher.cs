using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class ScopedQueryDispatcher : IQueryDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedQueryDispatcher(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var handleAsync = handlerType.GetMethod("HandleAsync")!;
        return await (Task<TResult>)handleAsync.Invoke(handler, [query, cancellationToken])!;
    }
}
