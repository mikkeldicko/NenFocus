namespace NenFocus.Application.UseCases.Sessions.RecordBlockedAttempt;

/// <summary>No Success flag, like the sweeps: nothing to record (no session, a repeat) is not a failure.</summary>
public sealed record RecordBlockedAttemptResult
{
    public bool Recorded { get; init; }
}
