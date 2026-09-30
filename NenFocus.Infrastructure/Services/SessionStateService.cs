using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;


namespace NenFocus.Infrastructure.Services;

public sealed class SessionStateService : ISessionStateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemClock _clock;
    private SessionDto? _activeSession;
    private bool _isRitualRequired;
    private Guid? _pendingFocusBlockId;

    public SessionDto? ActiveSession => _activeSession;
    public bool IsRitualRequired => _isRitualRequired;
    public Guid? PendingFocusBlockId => _pendingFocusBlockId;

    public event EventHandler<SessionDto?>? SessionChanged;
    public event EventHandler<RitualRequirementChange>? RitualRequirementChanged;
    public event EventHandler<IReadOnlyList<PostSessionDto>>? PostSessionsReady;
    public event EventHandler? PostSessionAcknowledged;
    public event EventHandler<IReadOnlyList<NenContractRecapDto>>? PostContractsReady;

    // Takes IServiceScopeFactory, not the scoped repositories directly — this service is
    // a Singleton (it must outlive any single operation to hold in-memory session state
    // and raise events across the app's whole lifetime), so it cannot capture Scoped
    // dependencies in its own constructor without pinning one EF Core context alive for
    // the entire app run. Mirrors ActiveSessionMonitor's AutoEndAsync/AutoResumeBreakAsync
    // and ScheduledEnforcementService's heartbeat — both already resolve their scoped
    // dependencies from a fresh per-operation scope for exactly this reason.
    public SessionStateService(IServiceScopeFactory scopeFactory, ISystemClock clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    public async Task InitializeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var presetRepository = scope.ServiceProvider.GetRequiredService<IBlockPresetRepository>();
        var sweepMissedFocusBlocks = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult>>();

        var session = await sessionRepository.GetActiveAsync();
        if (session != null)
        {
            var presetName = "Unknown Preset";
            if (session.BlockPresetId != Guid.Empty)
            {
                var preset = await presetRepository.GetByIdAsync(session.BlockPresetId);
                presetName = preset?.Name ?? "Unknown Preset";
            }

            _activeSession = MapToDto(session, presetName, _clock.Now);
            SessionChanged?.Invoke(this, _activeSession);
        }

        // Still creates the Missed Session records — that's still required regardless of
        // who discovers them afterward. What's gone is the event: ShellViewModel now finds
        // every unacknowledged post-session (missed or completed-but-undismissed alike) via
        // GetUnacknowledgedPostSessionsQuery at its own startup, ordered by EndedAt — a
        // freshly-swept Missed session has PostSessionAcknowledgedAt == null by construction,
        // so it surfaces there with no event needed to announce it.
        await sweepMissedFocusBlocks.HandleAsync(new SweepMissedFocusBlocksCommand());
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

    public void NotifyPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions)
    {
        if (postSessions.Count == 0) return;
        PostSessionsReady?.Invoke(this, postSessions);
    }

    public void NotifyPostSessionAcknowledged()
    {
        PostSessionAcknowledged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyPostContractsReady(IReadOnlyList<NenContractRecapDto> recaps)
    {
        if (recaps.Count == 0) return;
        PostContractsReady?.Invoke(this, recaps);
    }

    public void Clear()
    {
        _activeSession = null;
        _isRitualRequired = false;
        _pendingFocusBlockId = null;
        SessionChanged?.Invoke(this, null);
        RitualRequirementChanged?.Invoke(this, new RitualRequirementChange(false, null));
    }

    private static SessionDto MapToDto(Session s, string presetName, Moment now) => new SessionDto
    {
        Id = s.Id,
        Goal = s.Goal?.Value ?? string.Empty,
        PresetName = presetName,
        BlockPresetId = s.BlockPresetId,
        DurationMinutes = (int)s.Duration.Value.TotalMinutes,
        Status = s.Status,
        StartedAt = s.StartedAt ?? now,
        EndAt = s.EndAt ?? now,
        Breaks = SessionDto.MapBreaks(s.BreakSchedule),
        BreakPresetName = s.BreakPresetName,
        KeepBlockingDuringBreaks = s.KeepBlockingDuringBreaks
    };
}