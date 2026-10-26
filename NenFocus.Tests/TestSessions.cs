using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>
/// Sessions for tests that only need "one is running" or "this block already has one" — what the
/// repository's single doors return since Gap 92 (GetActiveAsync, GetByOriginatingBlockIdAsync).
/// Every session belongs to a block (Gap 114, D2), so each helper builds its block too.
/// </summary>
internal static class TestSessions
{
    private static readonly DateTime Morning = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>A spontaneous session running since 08:00 on 1 Jan 2026, on its walk-in block.</summary>
    public static Session Running()
    {
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Morning, Morning.AddHours(1)));
        return Session.Start(Guid.NewGuid(), new SessionGoal("Focus"), block, At.Utc(Morning));
    }

    /// <summary>A session that already belongs to <paramref name="focusBlockId"/>.</summary>
    public static Session For(Guid focusBlockId)
    {
        var block = FocusBlock.Create(focusBlockId, Guid.NewGuid(), At.Slot(Morning, Morning.AddHours(1)));
        return Session.Start(Guid.NewGuid(), new SessionGoal("Focus"), block, At.Utc(Morning));
    }
}
