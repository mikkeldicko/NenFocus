using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed record EndSessionResult : ICommandResult<EndSessionResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static EndSessionResult Ok() => new() { Success = true };
    public static EndSessionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };

    /// <summary>The end would fall at or before the start (Session.CanEndAt, Gap 89 Slice C) — before it only
    /// with the debug clock pushed back; at it, never in practice (the clock counts in 100 ns ticks), so the
    /// wording names the reachable case. This refusal's one wording home until Gap 100.</summary>
    public static EndSessionResult EndsBeforeStart() => Fail("A session can't end before it started.");
}
