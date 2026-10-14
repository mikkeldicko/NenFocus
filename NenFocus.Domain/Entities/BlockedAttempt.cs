using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// One attempt to reach a blocked app or site during a session — an append-only fact, its own small
/// aggregate so attempts arriving every few seconds never contend with the session's row (Gap 112, I3).
/// </summary>
public class BlockedAttempt : AggregateRoot
{
    /// <summary>A repeat of the same thing within this window is one attempt (redirects, a respawning app).</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(2);

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }

    /// <summary>
    /// The app's process name (e.g. "steam") or the site's domain (e.g. "facebook.com"), as
    /// BlockedApp and Website define them — the same form the block list holds.
    /// </summary>
    public string ResourceName { get; private set; } = string.Empty;

    public BlockedAttemptType AttemptType { get; private set; }

    /// <summary>
    /// UTC timestamp of when the attempt occurred.
    /// Passed in by the caller — domain never touches the clock.
    /// </summary>
    public Moment OccurredAt { get; private set; }

    private BlockedAttempt() { } // EF Core

    public static BlockedAttempt ForApp(Guid id, Guid sessionId, BlockedApp app, Moment occurredAt) =>
        new() { Id = id, SessionId = sessionId, ResourceName = app.ProcessName, AttemptType = BlockedAttemptType.App, OccurredAt = occurredAt };

    public static BlockedAttempt ForWebsite(Guid id, Guid sessionId, Website site, Moment occurredAt) =>
        new() { Id = id, SessionId = sessionId, ResourceName = site.Url, AttemptType = BlockedAttemptType.Website, OccurredAt = occurredAt };

    /// <summary>Whether this is the same attempt as <paramref name="previous"/>, reported again.</summary>
    public bool IsRepeatOf(BlockedAttempt previous) =>
        previous.SessionId == SessionId
        && previous.AttemptType == AttemptType
        && previous.ResourceName == ResourceName
        && OccurredAt - previous.OccurredAt < RepeatWindow;
}
