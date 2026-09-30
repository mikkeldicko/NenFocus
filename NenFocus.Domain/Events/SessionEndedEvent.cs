using NenFocus.Domain.ValueObjects;
namespace NenFocus.Domain.Events;

public class SessionEndedEvent : DomainEvent
{
    public Guid SessionId { get; }
    public Moment EndedAt { get; }

    public SessionEndedEvent(Guid sessionId, Moment endedAt)
        : base(endedAt)
    {
        SessionId = sessionId;
        EndedAt = endedAt;
    }
}