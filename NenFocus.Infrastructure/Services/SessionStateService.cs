using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;
using NenFocus.Domain.Entities;


namespace NenFocus.Infrastructure.Services;

public sealed class SessionStateService : ISessionStateService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IBlockPresetRepository _presetRepository;
    private readonly ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult> _sweepMissedFocusBlocks;
    private SessionDto? _activeSession;
    private bool _isRitualRequired;
    private Guid? _pendingFocusBlockId;

    public SessionDto? ActiveSession => _activeSession;
    public bool IsRitualRequired => _isRitualRequired;
    public Guid? PendingFocusBlockId => _pendingFocusBlockId;

    public event EventHandler<SessionDto?>? SessionChanged;
    public event EventHandler<RitualRequirementChange>? RitualRequirementChanged;
    public event EventHandler<IReadOnlyList<PostSessionDto>>? MissedSessionsDiscovered;

    public SessionStateService(
        ISessionRepository sessionRepository,
        IBlockPresetRepository presetRepository,
        ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult> sweepMissedFocusBlocks)
    {
        _sessionRepository = sessionRepository;
        _presetRepository = presetRepository;
        _sweepMissedFocusBlocks = sweepMissedFocusBlocks;
    }

    public async Task InitializeAsync()
    {
        var session = await _sessionRepository.GetActiveAsync();
        if (session != null)
        {
            var presetName = "Unknown Preset";
            if (session.BlockPresetId != Guid.Empty)
            {
                var preset = await _presetRepository.GetByIdAsync(session.BlockPresetId);
                presetName = preset?.Name ?? "Unknown Preset";
            }

            _activeSession = MapToDto(session, presetName);
            SessionChanged?.Invoke(this, _activeSession);
        }

        var sweepResult = await _sweepMissedFocusBlocks.HandleAsync(new SweepMissedFocusBlocksCommand());
        if (sweepResult.MissedSessions.Count > 0)
            MissedSessionsDiscovered?.Invoke(this, sweepResult.MissedSessions);
    }

    public void SetActive(SessionDto session)
    {
        _activeSession = session;
        _isRitualRequired = false;
        _pendingFocusBlockId = null;
        SessionChanged?.Invoke(this, session);
        RitualRequirementChanged?.Invoke(this, new RitualRequirementChange(false, null));
    }

    public void SetRitualRequired(bool required, Guid? focusBlockId = null)
    {
        if (!required)
        {
            if (!_isRitualRequired) return; // false -> false: no-op

            var releasedBlockId = _pendingFocusBlockId;
            _pendingFocusBlockId = null;
            _isRitualRequired = false;
            RitualRequirementChanged?.Invoke(this, new RitualRequirementChange(false, releasedBlockId));
            return;
        }

        if (_isRitualRequired && _pendingFocusBlockId == focusBlockId)
            return; // A -> A: same block still pending, nothing changed.

        // A -> B (back-to-back blocks, no gap) carries the displaced block's id as a
        // handoff so the caller can resolve it — it never gets its own false transition.
        var handoffFromBlockId = _isRitualRequired ? _pendingFocusBlockId : null;
        _pendingFocusBlockId = focusBlockId;
        _isRitualRequired = true;
        RitualRequirementChanged?.Invoke(this, new RitualRequirementChange(true, focusBlockId, handoffFromBlockId));
    }

    public void Clear()
    {
        _activeSession = null;
        _isRitualRequired = false;
        _pendingFocusBlockId = null;
        SessionChanged?.Invoke(this, null);
        RitualRequirementChanged?.Invoke(this, new RitualRequirementChange(false, null));
    }

    private static SessionDto MapToDto(Session s, string presetName) => new SessionDto
    {
        Id = s.Id,
        Goal = s.Goal?.Value ?? string.Empty,
        PresetName = presetName,
        BlockPresetId = s.BlockPresetId,
        DurationMinutes = (int)s.Duration.Value.TotalMinutes,
        Status = s.Status,
        StartedAt = s.StartedAt ?? DateTime.UtcNow,
        BreaksTaken = s.BreaksTaken,
        BreakStartedAt = s.BreakStartedAt,
        BreakSettings = s.BreakSettings is not null
            ? new BreakSettingsDto
            {
                IntervalMinutes = s.BreakSettings.IntervalMinutes,
                BreakDurationMinutes = s.BreakSettings.BreakLengthMinutes
            }
            : null
    };
}