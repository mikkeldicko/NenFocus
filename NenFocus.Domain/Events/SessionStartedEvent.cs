namespace NenFocus.Domain.Events;

public class SessionStartedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public Guid BlockProfileId { get; }
    public string Goal { get; }

    public SessionStartedEvent(Guid sessionId, Guid blockProfileId, string goal)
    {
        SessionId = sessionId;
        BlockProfileId = blockProfileId;
        Goal = goal;
    }
}