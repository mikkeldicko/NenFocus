using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public class BlockedAttempt
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }

    /// <summary>
    /// The name of the process (e.g. "chrome") or domain (e.g. "facebook.com")
    /// that was blocked.
    /// </summary>
    public string ResourceName { get; private set; } = string.Empty;

    public BlockedAttemptType AttemptType { get; private set; }

    /// <summary>
    /// UTC timestamp of when the attempt occurred.
    /// Passed in by the caller — domain never touches the clock.
    /// </summary>
    public Moment OccurredAt { get; private set; }

    private BlockedAttempt() { } // EF Core

    public static BlockedAttempt Record(
        Guid id,
        Guid sessionId,
        string resourceName,
        BlockedAttemptType attemptType,
        Moment occurredAt)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException(
                "Resource name cannot be empty.", nameof(resourceName));

        return new BlockedAttempt
        {
            Id = id,
            SessionId = sessionId,
            ResourceName = resourceName.Trim(),
            AttemptType = attemptType,
            OccurredAt = occurredAt
        };
    }
}