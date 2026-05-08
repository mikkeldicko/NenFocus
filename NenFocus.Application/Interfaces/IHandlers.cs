namespace NenFocus.Application.Interfaces;

/// <summary>
/// Generic command handler contract.
/// Each use case command has exactly one handler implementing this interface.
/// Register handlers in DI as: services.AddScoped&lt;ICommandHandler&lt;TCommand, TResult&gt;, THandler&gt;()
/// </summary>
/// <typeparam name="TCommand">The command record type (input).</typeparam>
/// <typeparam name="TResult">The result type returned after execution.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : notnull
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic query handler contract.
/// Each query has exactly one handler implementing this interface.
/// Register handlers in DI as: services.AddScoped&lt;IQueryHandler&lt;TQuery, TResult&gt;, THandler&gt;()
/// </summary>
/// <typeparam name="TQuery">The query record type (input).</typeparam>
/// <typeparam name="TResult">The result type returned by the query.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : notnull
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for commands that return no meaningful result.
/// Use when the command's only output is a side effect (e.g. DeleteBlockProfileCommand).
/// TResult will be bool (true = success) or a Result type if you add one later.
/// </summary>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, bool>
    where TCommand : notnull
{
}
