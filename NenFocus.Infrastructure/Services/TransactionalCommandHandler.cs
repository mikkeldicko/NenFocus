using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class TransactionalCommandHandler<TCommand, TResult>
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IUnitOfWork _uow;

    public TransactionalCommandHandler(
        ICommandHandler<TCommand, TResult> inner,
        IUnitOfWork uow)
    {
        _inner = inner;
        _uow = uow;
    }

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _inner.HandleAsync(command, cancellationToken);
        await _uow.CommitAsync(cancellationToken);
        return result;
    }
}
