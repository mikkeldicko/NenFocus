using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

public interface IBlockedAttemptTracker
{
    /// <summary>Records an app kill and persists it to the DB.</summary>
    Task RecordAppBlockedAsync(Guid sessionId, string processName);

    /// <summary>Records a website interception and persists it to the DB.</summary>
    Task RecordWebBlockedAsync(Guid sessionId, string domain);

    /// <summary>Retrieves all history for a specific session.</summary>
    Task<List<BlockedAttempt>> GetAttemptsForSessionAsync(Guid sessionId);
}