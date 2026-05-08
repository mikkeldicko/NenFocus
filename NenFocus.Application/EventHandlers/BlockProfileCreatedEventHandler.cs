using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

/// <summary>
/// Fires when a new block profile is created.
/// Hook for Insights milestone tracking (e.g. "Created your first profile").
/// Extend this handler when the Insights/milestones feature is implemented.
/// </summary>
public sealed class BlockProfileCreatedEventHandler
{
    public Task HandleAsync(
        BlockProfileCreatedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // Placeholder for Insights milestone logic:
        // - Check if this is the user's first profile → award milestone badge
        // - Log profile creation for weekly recap stats
        // - Trigger any onboarding flow if applicable

        return Task.CompletedTask;
    }
}
