namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// Ties a redemption Focus Block to one specific missed or late Focus Block it covers, and how much
/// of it — whole minutes, above zero (Gap 92, V10). Written only by the Domain (Gap 106).
/// </summary>
public record RedemptionEntry
{
    public Guid MissedBlockId { get; }
    public WholeMinutes Covered { get; }

    public RedemptionEntry(Guid missedBlockId, WholeMinutes covered)
    {
        if (covered <= WholeMinutes.Zero)
            throw new ArgumentException("A redemption entry must cover something.", nameof(covered));

        MissedBlockId = missedBlockId;
        Covered = covered;
    }
}
