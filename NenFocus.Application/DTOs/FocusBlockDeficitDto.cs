using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// A missed or late Focus Block still carrying uncovered deficit — the "Redemption"
/// checklist's row shape. Blocks whose deficit has been fully covered by linked
/// redemption blocks are not included.
/// </summary>
public sealed record FocusBlockDeficitDto
{
    public Guid FocusBlockId { get; init; }
    public Guid BlockPresetId { get; init; }
    public DateTime ScheduledStart { get; init; }
    public DateTime ScheduledEnd { get; init; }
    public FocusBlockOutcome Outcome { get; init; }
    public double RemainingDeficitMinutes { get; init; }
    public double PlannedMinutes { get; init; }
    public double CompletedMinutes { get; init; }
}
