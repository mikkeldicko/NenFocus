namespace NenFocus.Domain.Events;

public class SessionEndedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public DateTime EndedAt { get; }

    public SessionEndedEvent(Guid sessionId, DateTime endedAt)
        : base(endedAt)
    {
        SessionId = sessionId;
        EndedAt = endedAt;
    }
}