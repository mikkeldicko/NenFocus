using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed record StartSessionCommand : ICommand<StartSessionResult>
{
    public string Goal { get; init; } = string.Empty;
    public Guid? PresetId { get; init; }
    public int DurationMinutes { get; init; }

    /// <summary>
    /// Null means "No timer" — the session runs with no breaks at all. The handler
    /// lays the preset's BreakTimer over the session's run to produce the concrete
    /// BreakSchedule the Session stores (plus a snapshot of the preset's name);
    /// nothing downstream holds a live reference back to this id (Gap 63).
    /// </summary>
    public Guid? BreakTimerPresetId { get; init; }
    public Guid? OriginatingBlockId { get; init; }
    public IReadOnlyList<string> AdditionalBlockedSites { get; init; } = [];
    public IReadOnlyList<string> AdditionalBlockedApps { get; init; } = [];
}
