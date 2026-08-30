using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class ScopedCommandDispatcher : ICommandDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedCommandDispatcher(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Resolves the *interface* type, not the concrete handler — RegisterDecoratedCommandHandlers
        // registers ICommandHandler<,> itself as the already-decorated (Transactional +
        // Validating) instance, so this gets the same wrapped behavior any direct injection
        // of ICommandHandler<TCommand,TResult> would have gotten, unchanged.
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var handleAsync = handlerType.GetMethod("HandleAsync")!;
        return await (Task<TResult>)handleAsync.Invoke(handler, [command, cancellationToken])!;
    }
}
