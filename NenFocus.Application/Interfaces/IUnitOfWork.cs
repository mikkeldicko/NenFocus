namespace NenFocus.Application.Interfaces;

public interface IUnitOfWork
{
    /// <summary>Saves every change made since the last commit, then publishes the domain events.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Throws away every change made since the last commit — a failed command saves nothing (Gap 110).</summary>
    void Discard();
}
