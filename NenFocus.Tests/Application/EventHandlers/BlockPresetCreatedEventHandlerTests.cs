using FluentAssertions;
using NenFocus.Application.EventHandlers;
using NenFocus.Domain.Events;

namespace NenFocus.Tests.Application.EventHandlers;

public class BlockPresetCreatedEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_IsANoOpThatCompletes()
    {
        var handler = new BlockPresetCreatedEventHandler();

        var act = () => handler.HandleAsync(new BlockPresetCreatedEvent(Guid.NewGuid(), "Deep Work", DateTime.UtcNow));

        await act.Should().NotThrowAsync();
    }
}
