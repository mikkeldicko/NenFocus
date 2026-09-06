using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Feeds the Nen Contract popover — one row per in-effect (Active or Scheduled) contract,
/// app-wide and independent of whatever week the Planner grid currently has on screen.
/// Status distinguishes the single Active row (if any) from the Upcoming ones.
/// </summary>
public sealed record NenContractSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }
    public NenContractStatus Status { get; init; }
}
