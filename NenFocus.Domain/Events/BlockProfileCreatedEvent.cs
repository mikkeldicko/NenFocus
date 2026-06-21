namespace NenFocus.Domain.Events;

public class BlockProfileCreatedEvent : DomainEvent
{
    public Guid ProfileId { get; }
    public string Name { get; }

    public BlockProfileCreatedEvent(Guid profileId, string name, DateTime occurredAt)
        : base(occurredAt)
    {
        ProfileId = profileId;
        Name = name;
    }
}