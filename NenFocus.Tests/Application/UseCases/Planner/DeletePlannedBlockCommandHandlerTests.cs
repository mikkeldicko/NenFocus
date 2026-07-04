using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class DeletePlannedBlockCommandHandlerTests
{
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();

    private DeletePlannedBlockCommandHandler CreateHandler() => new(_blocks);

    [Fact]
    public async Task HandleAsync_HappyPath_DeletesBlockAndReturnsOk()
    {
        var blockId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = blockId });

        result.Success.Should().BeTrue();
        await _blocks.Received(1).DeleteAsync(blockId, Arg.Any<CancellationToken>());
    }
}
