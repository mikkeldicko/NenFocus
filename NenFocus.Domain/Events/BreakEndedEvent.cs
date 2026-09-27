namespace NenFocus.Domain.Events;

public class BreakEndedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public int BreakNumber { get; }

    public BreakEndedEvent(Guid sessionId, int breakNumber, DateTime occurredAt)
        : base(occurredAt)
    {
        SessionId = sessionId;
        BreakNumber = breakNumber;
    }
}
