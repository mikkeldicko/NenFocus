using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.RecordBlockedAttempt;

/// <summary>
/// What enforcement saw: something blocked was reached, when, and under which policy (Gap 112, I3).
/// Enforcement reports; this use case decides what the attempt belongs to — so a change to that
/// (Gap 117: which block caught it) touches only the handler and the Domain.
/// </summary>
public sealed record RecordBlockedAttemptCommand : ICommand<RecordBlockedAttemptResult>
{
    /// <summary>The listed app's process name or the visited site's domain.</summary>
    public required string Resource { get; init; }

    public required BlockedAttemptType Kind { get; init; }

    public required Moment OccurredAt { get; init; }

    /// <summary>
    /// The policy that was enforcing at <see cref="OccurredAt"/> — not looked up again here: attempts are
    /// queued, so by the time this runs a session may have ended or another begun.
    /// </summary>
    public required ActivePolicySlice Policy { get; init; }
}
