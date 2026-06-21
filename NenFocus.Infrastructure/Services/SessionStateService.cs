using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;


namespace NenFocus.Infrastructure.Services;

public sealed class SessionStateService : ISessionStateService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IBlockProfileRepository _profileRepository;
    private SessionDto? _activeSession;
    private bool _isRitualRequired;

    public SessionDto? ActiveSession => _activeSession;
    public bool IsRitualRequired => _isRitualRequired;

    public event EventHandler<SessionDto?>? SessionChanged;
    public event EventHandler<bool>? RitualRequirementChanged;

    public SessionStateService(
        ISessionRepository sessionRepository,
        IBlockProfileRepository profileRepository)
    {
        _sessionRepository = sessionRepository;
        _profileRepository = profileRepository;
    }

    public async Task InitializeAsync()
    {
        var session = await _sessionRepository.GetActiveAsync();
        if (session != null)
        {
            var profileName = "Unknown Profile";
            if (session.BlockProfileId != Guid.Empty)
            {
                var profile = await _profileRepository.GetByIdAsync(session.BlockProfileId);
                profileName = profile?.Name ?? "Unknown Profile";
            }

            _activeSession = MapToDto(session, profileName);
            SessionChanged?.Invoke(this, _activeSession);
        }
    }

    public void SetActive(SessionDto session)
    {
        _activeSession = session;
        _isRitualRequired = false;
        SessionChanged?.Invoke(this, session);
        RitualRequirementChanged?.Invoke(this, false);
    }

    public void SetRitualRequired(bool required)
    {
        if (_isRitualRequired == required) return;
        _isRitualRequired = required;
        RitualRequirementChanged?.Invoke(this, required);
    }

    public void Clear()
    {
        _activeSession = null;
        _isRitualRequired = false;
        SessionChanged?.Invoke(this, null);
        RitualRequirementChanged?.Invoke(this, false);
    }

    private static SessionDto MapToDto(Session s, string profileName) => new SessionDto
    {
        Id = s.Id,
        Goal = s.Goal?.Value ?? string.Empty,
        ProfileName = profileName,
        DurationMinutes = (int)s.Duration.Value.TotalMinutes,
        Status = s.Status,
        StartedAt = s.StartedAt ?? DateTime.UtcNow,
        BreaksTaken = s.BreaksTaken,
        BreakStartedAt = s.BreakStartedAt,
        BreakSettings = s.BreakSettings is not null
            ? new BreakSettingsDto
            {
                Enabled = true,
                IntervalMinutes = s.BreakSettings.IntervalMinutes,
                BreakDurationMinutes = s.BreakSettings.BreakLengthMinutes
            }
            : null
    };
}