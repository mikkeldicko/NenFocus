namespace NenFocus.Application.Interfaces;

/// <summary>
/// Marker interface for queries. A query is a read operation that returns <typeparamref name="TResult"/>.
/// Pair with <see cref="IQueryHandler{TQuery, TResult}"/>.
/// </summary>
public interface IQuery<TResult>;