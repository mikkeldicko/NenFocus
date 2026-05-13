namespace NenFocus.Application.Interfaces;

/// <summary>
/// Marker interface for commands. A command is a write operation that returns <typeparamref name="TResult"/>.
/// Pair with <see cref="ICommandHandler{TCommand, TResult}"/>.
/// </summary>
public interface ICommand<TResult>;