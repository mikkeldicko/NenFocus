using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Tells the user about every planned-block marker (starting soon, started, ended) the
/// clock passed in (<c>from</c>, <c>to</c>] — see <c>PlannedBlock.MarkersPassedBetween</c>.
/// Called each heartbeat tick with the previous tick's end as <c>from</c>, so each marker
/// is announced once.
/// </summary>
public interface IBlockMarkerNotifier
{
    Task NotifyPassedAsync(Moment from, Moment to, CancellationToken ct = default);
}
