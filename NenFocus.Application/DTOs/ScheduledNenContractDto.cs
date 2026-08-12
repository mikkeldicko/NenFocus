using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Feeds the Nen Contract popover's "Upcoming" list — one row per not-yet-started contract.
/// </summary>
public sealed record ScheduledNenContractDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }
}
