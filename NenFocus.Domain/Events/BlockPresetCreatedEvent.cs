namespace NenFocus.Domain.Events;

public class BlockPresetCreatedEvent : DomainEvent
{
    public Guid PresetId { get; }
    public string Name { get; }

    public BlockPresetCreatedEvent(Guid presetId, string name, DateTime occurredAt)
        : base(occurredAt)
    {
        PresetId = presetId;
        Name = name;
    }
}