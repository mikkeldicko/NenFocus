namespace NenFocus.Domain.Events;

public class BreakStartedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public int BreakNumber { get; }

    public BreakStartedEvent(Guid sessionId, int breakNumber)
    {
        SessionId = sessionId;
        BreakNumber = breakNumber;
    }
}