using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.AutoEndSession;

/// <summary>
/// The clock's way of ending a session: if the running session's time is up at
/// <see cref="AsOf"/>, end it. Dispatched by the heartbeat every tick — usually nothing is due.
/// Separate from EndSessionCommand (you end it, now, whatever its time): a future Strict Mode
/// refuses yours and never the clock's (Gap 89 Slice A).
/// </summary>
public sealed record AutoEndSessionCommand : ICommand<AutoEndSessionResult>
{
    /// <summary>The heartbeat tick's one "now".</summary>
    public required Moment AsOf { get; init; }
}
