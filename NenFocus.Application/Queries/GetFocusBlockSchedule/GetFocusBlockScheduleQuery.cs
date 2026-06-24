using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetFocusBlockSchedule;

public sealed record GetFocusBlockScheduleQuery : IQuery<FocusBlockSchedule?>
{
    public Guid FocusBlockId { get; init; }
}
