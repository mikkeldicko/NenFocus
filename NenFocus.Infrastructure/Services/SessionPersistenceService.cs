using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NenFocus.Application.DTOs;
using NenFocus.Domain.Enums;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Persists the active session end time to a small JSON file in %AppData%\NenFocus\.
/// On restart, if the end time hasn't passed, the session is restored.
/// </summary>
public sealed class SessionPersistenceService
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record PersistedSession
    {
        public Guid Id { get; init; }
        public string Goal { get; init; } = string.Empty;
        public string ProfileName { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; }
        public DateTime EndTime { get; init; }
        public int DurationMinutes { get; init; }
        public SessionStatus Status { get; init; } = SessionStatus.Active;
        public DateTime? BreakStartedAt { get; init; }
        public int BreaksTaken { get; init; }
        public BreakSettingsDto? BreakSettings { get; init; }
    }

    public SessionPersistenceService(string appDataFolder)
    {
        _filePath = Path.Combine(appDataFolder, "active_session.json");
    }

    /// <summary>Saves the active session to disk.</summary>
    public void Save(SessionDto session, DateTime endTime)
    {
        var data = new PersistedSession
        {
            Id = session.Id,
            Goal = session.Goal,
            ProfileName = session.ProfileName,
            StartedAt = session.StartedAt,
            EndTime = endTime,
            DurationMinutes = session.DurationMinutes,
            Status = session.Status,
            BreakStartedAt = session.BreakStartedAt,
            BreaksTaken = session.BreaksTaken,
            BreakSettings = session.BreakSettings
        };

        File.WriteAllText(_filePath, JsonSerializer.Serialize(data, _jsonOptions));
    }

    /// <summary>
    /// Tries to restore a session from disk.
    /// Returns null if no session file exists or the session has already expired.
    /// </summary>
    public SessionDto? TryRestore()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<PersistedSession>(json, _jsonOptions);

            if (data is null || data.EndTime <= DateTime.UtcNow)
            {
                Clear();
                return null;
            }

            // If we were on break and the break duration has elapsed, restore as Active
            var status = data.Status;
            DateTime? breakStartedAt = data.BreakStartedAt;

            if (status == SessionStatus.OnBreak && breakStartedAt.HasValue && data.BreakSettings is not null)
            {
                var breakEnd = breakStartedAt.Value.AddMinutes(data.BreakSettings.BreakDurationMinutes);
                if (DateTime.UtcNow >= breakEnd)
                {
                    // Break expired while app was closed — resume automatically
                    status = SessionStatus.Active;
                    breakStartedAt = null;
                }
            }

            return new SessionDto
            {
                Id = data.Id,
                Goal = data.Goal,
                ProfileName = data.ProfileName,
                StartedAt = data.StartedAt,
                DurationMinutes = data.DurationMinutes,
                Status = status,
                BreakStartedAt = breakStartedAt,
                BreaksTaken = data.BreaksTaken,
                BreakSettings = data.BreakSettings
            };
        }
        catch
        {
            Clear();
            return null;
        }
    }

    /// <summary>Deletes the persisted session file.</summary>
    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}