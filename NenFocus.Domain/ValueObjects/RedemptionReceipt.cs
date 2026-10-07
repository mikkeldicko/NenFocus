namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A redemption block's promise to pay: which missed blocks it covers, and how much of each.
/// Stored with the block and fixed once written — the missed block's deficit is the debt
/// (worked out, changes as payments land or fail); this is the receipt. Owns the total and
/// "how much for this missed block", so no caller sums the entries itself (Gap 106).
/// </summary>
public sealed class RedemptionReceipt : IEquatable<RedemptionReceipt>
{
    public static RedemptionReceipt None { get; } = new([]);

    public IReadOnlyList<RedemptionEntry> Entries { get; }

    public RedemptionReceipt(IEnumerable<RedemptionEntry> entries)
    {
        var list = entries.ToList();
        if (list.Select(e => e.MissedBlockId).Distinct().Count() != list.Count)
            throw new ArgumentException("A receipt lists each missed block once.", nameof(entries));

        Entries = list;
    }

    public double TotalMinutes => Entries.Sum(e => e.MinutesCovered);

    public double MinutesFor(Guid missedBlockId)
        => Entries.FirstOrDefault(e => e.MissedBlockId == missedBlockId)?.MinutesCovered ?? 0;

    /// <summary>
    /// Divides the receipt across a split whose first piece is <paramref name="firstLength"/> long:
    /// the first piece fills first, in entry order; an entry straddling the cut is split in two
    /// against the same missed block. Nothing is recalculated or lost (Gap 106, decision 2).
    /// </summary>
    public (RedemptionReceipt First, RedemptionReceipt Second) SplitAt(TimeSpan firstLength)
    {
        var first = new List<RedemptionEntry>();
        var second = new List<RedemptionEntry>();
        var room = firstLength.TotalMinutes;

        foreach (var entry in Entries)
        {
            var toFirst = Math.Min(room, entry.MinutesCovered);
            if (toFirst > 0)
            {
                first.Add(new RedemptionEntry(entry.MissedBlockId, toFirst));
                room -= toFirst;
            }

            var toSecond = entry.MinutesCovered - toFirst;
            if (toSecond > 0)
                second.Add(new RedemptionEntry(entry.MissedBlockId, toSecond));
        }

        return (new RedemptionReceipt(first), new RedemptionReceipt(second));
    }

    public bool Equals(RedemptionReceipt? other)
        => other is not null && Entries.SequenceEqual(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as RedemptionReceipt);

    public override int GetHashCode()
        => Entries.Aggregate(0, (hash, entry) => HashCode.Combine(hash, entry));
}
