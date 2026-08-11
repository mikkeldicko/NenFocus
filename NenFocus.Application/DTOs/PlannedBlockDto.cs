using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PlannedBlockDto
{
    public Guid Id { get; init; }
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;

    public BlockPresetType BlockPresetType { get; init; }
    public WebsiteFilterType WebsiteFilterType { get; init; }

    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public bool IsCompleted { get; init; }

    /// <summary>
    /// Mirrors PlannedBlock.HasEnded(now) — the same predicate DeletePlannedBlockValidator
    /// enforces. The UI binds to this rather than deriving its own notion of "past" from
    /// Start/End, so the two can never drift apart.
    /// </summary>
    public Guid? ContractId { get; init; }
    public bool HasEnded { get; init; }

    /// <summary>
    /// True when ContractId resolves to a contract that is still a live commitment
    /// (NenContract.IsInEffect — Scheduled or Active). Mirrors DeletePlannedBlockValidator/
    /// UpdatePlannedBlockValidator's own resolution exactly, so the UI never re-derives
    /// "is this block locked by its contract" independently of the Application-layer rule.
    /// </summary>
    public bool IsUnderInEffectContract { get; init; }

    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}
