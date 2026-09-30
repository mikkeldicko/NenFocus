using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Events;

public class SessionMissedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public Moment ScheduledStart { get; }
    public Moment ScheduledEnd { get; }

    public SessionMissedEvent(Guid sessionId, Moment scheduledStart, Moment scheduledEnd)
        : base(scheduledEnd)
    {
        SessionId = sessionId;
        ScheduledStart = scheduledStart;
        ScheduledEnd = scheduledEnd;
    }
}
