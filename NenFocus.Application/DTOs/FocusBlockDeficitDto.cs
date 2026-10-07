using NenFocus.Domain.ValueObjects;
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
    public TimeSlot Scheduled { get; init; } = null!;
    public FocusBlockOutcome Outcome { get; init; }
    public double RemainingDeficitMinutes { get; init; }
    public double PlannedMinutes { get; init; }
    public double CompletedMinutes { get; init; }

    /// <summary>Planned is the scheduled length; completed is what of it wasn't owed.</summary>
    public static FocusBlockDeficitDto From(OutstandingDeficit deficit)
    {
        var planned = deficit.Scheduled.Length.TotalMinutes;
        return new FocusBlockDeficitDto
        {
            FocusBlockId = deficit.MissedBlockId,
            BlockPresetId = deficit.PresetId,
            Scheduled = deficit.Scheduled,
            Outcome = deficit.Outcome,
            RemainingDeficitMinutes = deficit.RemainingMinutes,
            PlannedMinutes = planned,
            CompletedMinutes = planned - deficit.OwedMinutes
        };
    }
}
