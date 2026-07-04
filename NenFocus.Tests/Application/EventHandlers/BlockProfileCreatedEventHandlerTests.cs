using FluentAssertions;
using NenFocus.Application.EventHandlers;
using NenFocus.Domain.Events;

namespace NenFocus.Tests.Application.EventHandlers;

public class BlockProfileCreatedEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_IsANoOpThatCompletes()
    {
        var handler = new BlockProfileCreatedEventHandler();

        var act = () => handler.HandleAsync(new BlockProfileCreatedEvent(Guid.NewGuid(), "Deep Work", DateTime.UtcNow));

        await act.Should().NotThrowAsync();
    }
}
