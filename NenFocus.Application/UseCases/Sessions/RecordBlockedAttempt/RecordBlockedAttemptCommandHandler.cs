using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.RecordBlockedAttempt;

public sealed class RecordBlockedAttemptCommandHandler
    : ICommandHandler<RecordBlockedAttemptCommand, RecordBlockedAttemptResult>
{
    private static readonly RecordBlockedAttemptResult NotRecorded = new() { Recorded = false };

    private readonly IBlockedAttemptRepository _attempts;

    public RecordBlockedAttemptCommandHandler(IBlockedAttemptRepository attempts)
        => _attempts = attempts;

    public async Task<RecordBlockedAttemptResult> HandleAsync(
        RecordBlockedAttemptCommand command,
        CancellationToken ct = default)
    {
        // Only attempts inside a session are tracked today; which block caught it is Gap 117's.
        if (command.Policy.ActiveSessionId is not { } sessionId)
            return NotRecorded;

        if (AttemptFrom(command, sessionId) is not { } attempt)
            return NotRecorded;

        var latest = await _attempts.GetLatestAsync(sessionId, attempt.ResourceName, ct);
        if (latest is not null && attempt.IsRepeatOf(latest))
            return NotRecorded;

        _attempts.Add(attempt);
        return new RecordBlockedAttemptResult { Recorded = true };
    }

    // An app or a site as the block list defines them (Gap 112); anything that is neither isn't an attempt.
    private static BlockedAttempt? AttemptFrom(RecordBlockedAttemptCommand command, Guid sessionId) => command.Kind switch
    {
        BlockedAttemptType.App when BlockedApp.Check(command.Resource) is null =>
            BlockedAttempt.ForApp(Guid.NewGuid(), sessionId, new BlockedApp(command.Resource), command.OccurredAt),
        BlockedAttemptType.Website when Website.TryCreate(command.Resource, out var site) =>
            BlockedAttempt.ForWebsite(Guid.NewGuid(), sessionId, site, command.OccurredAt),
        _ => null,
    };
}
