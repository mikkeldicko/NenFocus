using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Infrastructure.Services;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Services;

public class ValidatingCommandHandlerTests
{
    private readonly ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> _inner =
        Substitute.For<ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult>>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    private ValidatingCommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> CreateHandler()
        => new(_inner, _serviceProvider);

    [Fact]
    public async Task HandleAsync_NoValidatorRegistered_CallsInnerHandlerDirectly()
    {
        _serviceProvider.GetService(typeof(IValidator<DeletePlannedBlockCommand>)).Returns((object?)null);
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Ok());

        var result = await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.Success.Should().BeTrue();
        await _inner.Received(1).HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidatorRejects_ReturnsFailWithoutCallingInner()
    {
        var validator = Substitute.For<IValidator<DeletePlannedBlockCommand>>();
        validator.ValidateAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("BlockId", "Block ID is required.")]));
        _serviceProvider.GetService(typeof(IValidator<DeletePlannedBlockCommand>)).Returns(validator);

        var result = await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.Empty });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Block ID is required.");
        await _inner.DidNotReceive().HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidatorAccepts_CallsInnerAndReturnsItsResult()
    {
        var validator = Substitute.For<IValidator<DeletePlannedBlockCommand>>();
        validator.ValidateAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _serviceProvider.GetService(typeof(IValidator<DeletePlannedBlockCommand>)).Returns(validator);
        var expected = DeletePlannedBlockResult.Ok();
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.Should().BeSameAs(expected);
    }
}
