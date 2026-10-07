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

    /// <summary>What is still owed once redemptions have paid — whole minutes (Gap 92, V10).</summary>
    public WholeMinutes RemainingDeficit { get; init; }

    public static FocusBlockDeficitDto From(OutstandingDeficit deficit) => new()
    {
        FocusBlockId = deficit.MissedBlockId,
        BlockPresetId = deficit.PresetId,
        Scheduled = deficit.Scheduled,
        Outcome = deficit.Outcome,
        RemainingDeficit = deficit.Remaining
    };
}
