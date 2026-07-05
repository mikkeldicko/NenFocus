namespace NenFocus.Domain.Events;

public class SessionMissedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public DateTime ScheduledStart { get; }
    public DateTime ScheduledEnd { get; }

    public SessionMissedEvent(Guid sessionId, DateTime scheduledStart, DateTime scheduledEnd)
        : base(scheduledEnd)
    {
        SessionId = sessionId;
        ScheduledStart = scheduledStart;
        ScheduledEnd = scheduledEnd;
    }
}
