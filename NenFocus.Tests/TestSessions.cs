using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>
/// Sessions for tests that only need "one is running" or "this block already has one" — what the
/// repository's single doors return since Gap 92 (GetActiveAsync, GetByOriginatingBlockIdAsync).
/// </summary>
internal static class TestSessions
{
    /// <summary>A spontaneous session running since 08:00 on 1 Jan 2026.</summary>
    public static Session Running()
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
        return session;
    }

    /// <summary>A session that already belongs to <paramref name="focusBlockId"/> (not started).</summary>
    public static Session For(Guid focusBlockId) =>
        Session.Create(Guid.NewGuid(), new SessionGoal("Focus"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: focusBlockId);
}
