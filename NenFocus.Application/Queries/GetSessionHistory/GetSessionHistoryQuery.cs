using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetSessionHistory;

public sealed record GetSessionHistoryQuery : IQuery<IReadOnlyList<SessionSummaryDto>>
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public Guid? PresetId { get; init; }
}
