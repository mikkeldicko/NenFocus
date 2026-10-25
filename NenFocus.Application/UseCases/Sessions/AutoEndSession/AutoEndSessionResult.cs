namespace NenFocus.Application.UseCases.Sessions.AutoEndSession;

/// <summary>Like the other sweeps' results, no Success flag: nothing due is the usual answer, not a failure.</summary>
public sealed record AutoEndSessionResult
{
    /// <summary>The session this tick ended, or null when nothing was due.</summary>
    public Guid? EndedSessionId { get; init; }
}
