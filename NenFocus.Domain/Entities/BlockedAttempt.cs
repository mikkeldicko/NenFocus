using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Entities;

public class BlockedAttempt : BaseEntity
{
    // The ID of the session during which this happened
    public Guid SessionId { get; private set; }

    // The name of the process (e.g., "chrome.exe") or domain (e.g., "facebook.com")
    public string ResourceName { get; private set; } = string.Empty;

    public BlockedAttemptType AttemptType { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    // Parameterless constructor for EF Core
    private BlockedAttempt() { }

    public BlockedAttempt(Guid sessionId, string resourceName, BlockedAttemptType type)
    {
        SessionId = sessionId;
        ResourceName = resourceName;
        AttemptType = type;
        Timestamp = DateTimeOffset.Now;
    }
}