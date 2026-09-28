namespace NenFocus.Application.Interfaces;

/// <summary>
/// Tells the user about every planned-block boundary (starting soon, started, ended) the
/// clock crossed in (<c>from</c>, <c>to</c>] — see <c>PlannedBlock.TransitionsBetween</c>.
/// Called each heartbeat tick with the previous tick's end as <c>from</c>, so each boundary
/// is announced once.
/// </summary>
public interface IBlockTransitionHandler
{
    Task HandleAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
