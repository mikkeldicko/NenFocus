namespace NenFocus.Domain.Events;

public class SessionStartedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public Guid BlockPresetId { get; }
    public string Goal { get; }

    public SessionStartedEvent(Guid sessionId, Guid blockPresetId, string goal, DateTime occurredAt)
        : base(occurredAt)
    {
        SessionId = sessionId;
        BlockPresetId = blockPresetId;
        Goal = goal;
    }
}