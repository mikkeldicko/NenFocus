using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using System.Reflection;

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

        // A failed command saves nothing (Gap 110): whatever it changed before failing is thrown
        // away, so it can't be committed now or ride along on the next command in this scope.
        // Results without a Success flag (the sweeps) never fail.
        if (result is ICommandResult { Success: false })
        {
            _uow.Discard();
            return result;
        }

        try
        {
            await _uow.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // The inner handler's own ExistsWithNameAsync-style check already caught the
            // common case before this point — this only fires on a genuine race (two
            // commands committing between each other's check and insert). Translated
            // generically here, once, rather than in every command that happens to touch
            // a uniquely-constrained column, mirroring how ValidatingCommandHandler already
            // uses the same ICommandResult<TResult>.Fail contract generically.
            var failMethod = typeof(TResult).GetMethod(
                "Fail", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
            if (failMethod is null) throw;

            return (TResult)failMethod.Invoke(null, ["That name is already in use."])!;
        }

        return result;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }; // SQLITE_CONSTRAINT_UNIQUE
}
