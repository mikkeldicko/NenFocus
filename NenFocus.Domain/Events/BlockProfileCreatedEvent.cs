namespace NenFocus.Domain.Events;

public class BlockProfileCreatedEvent : DomainEvent
{
    public Guid ProfileId { get; }
    public string Name { get; }

    public BlockProfileCreatedEvent(Guid profileId, string name)
    {
        ProfileId = profileId;
        Name = name;
    }
}