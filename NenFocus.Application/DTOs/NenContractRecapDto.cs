using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Structural facts + performance grade about a Completed or Broken contract — the "here's
/// how it went" summary for the Post-Contract recap screen (and, later, Insights' contract
/// history — same query, no changes needed to reuse it there). Read-only, computed at query
/// time from already-persisted facts; never itself persisted.
/// </summary>
public sealed record NenContractRecapDto
{
    public Guid ContractId { get; init; }
    public string Name { get; init; } = string.Empty;
    public NenContractStatus Status { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }

    /// <summary>The frozen Seal-time target — NenContract.CommittedFocusHours.</summary>
    public double CommittedFocusHours { get; init; }

    /// <summary>Live sum of every contract-linked (committed or redemption) block's credited hours.</summary>
    public double TotalCompletedHours { get; init; }

    /// <summary>Completed hours from Bonus blocks (ContractId null) within the contract's window.</summary>
    public double TotalBonusHours { get; init; }

    /// <summary>
    /// TotalCompletedHours ÷ CommittedFocusHours, uncapped (133% shows as 133%). Null only
    /// when CommittedFocusHours is 0 — a contract with nothing ever planned, which can't be
    /// meaningfully graded.
    /// </summary>
    public double? CompletionPercentage { get; init; }

    /// <summary>
    /// "Completed" (100%+), "Partially Completed" (1-99%), or "Failed" (0% at a natural
    /// end) — deliberately never "Broken" for this label, which stays reserved for an
    /// actual cancellation (Status == Broken already says that on its own). Null alongside
    /// CompletionPercentage when nothing was ever planned.
    /// </summary>
    public string? CompletionGrade { get; init; }

    // ── Block counts — contract-linked (committed or redemption) blocks only ──
    public int TotalFocusBlocks { get; init; }
    public int TotalBackgroundBlocks { get; init; }
    public int RedemptionBlocks { get; init; }
    public int CompletedOnTimeBlocks { get; init; }
    public int StartedLateBlocks { get; init; }
    public int EndedEarlyBlocks { get; init; }
    public int MissedBlocks { get; init; }
    public int CancelledBlocks { get; init; }

    /// <summary>Bonus blocks (ContractId null) whose Start falls within the contract's window.</summary>
    public int BonusBlockCount { get; init; }

    public string? Reflection { get; init; }
}
