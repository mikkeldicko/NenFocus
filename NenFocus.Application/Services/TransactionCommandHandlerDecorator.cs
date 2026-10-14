using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Services;

public sealed class TransactionCommandHandlerDecorator<TCommand, TResult>
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IUnitOfWork _uow;

    public TransactionCommandHandlerDecorator(
        ICommandHandler<TCommand, TResult> inner,
        IUnitOfWork uow)
    {
        _inner = inner;
        _uow = uow;
    }

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _inner.HandleAsync(command, cancellationToken);

        // A failed command saves nothing (Gap 110): whatever it changed before failing is thrown
        // away, so it can't be committed now or ride along on the next command in this scope.
        // Results without a Success flag (the sweeps) never fail.
        if (result is ICommandResult { Success: false })
        {
            _uow.Discard();
            return result;
        }

        // A failed commit propagates, unique-index violations included (Gap 112): the indexes are
        // backstops behind the validators and the Domain, so one firing is a bug, not a refusal.
        await _uow.CommitAsync(cancellationToken);
        return result;
    }
}
