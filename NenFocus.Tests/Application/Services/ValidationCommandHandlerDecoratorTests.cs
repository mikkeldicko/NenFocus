using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeleteBlock;
using NenFocus.Application.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class ValidationCommandHandlerDecoratorTests
{
    private readonly ICommandHandler<DeleteBlockCommand, DeleteBlockResult> _inner =
        Substitute.For<ICommandHandler<DeleteBlockCommand, DeleteBlockResult>>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    private ValidationCommandHandlerDecorator<DeleteBlockCommand, DeleteBlockResult> CreateHandler()
        => new(_inner, _serviceProvider);

    [Fact]
    public async Task HandleAsync_NoValidatorRegistered_CallsInnerHandlerDirectly()
    {
        _serviceProvider.GetService(typeof(IValidator<DeleteBlockCommand>)).Returns((object?)null);
        _inner.HandleAsync(Arg.Any<DeleteBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeleteBlockResult.Ok());

        var result = await CreateHandler().HandleAsync(new DeleteBlockCommand { BlockId = Guid.NewGuid() });

        result.Success.Should().BeTrue();
        await _inner.Received(1).HandleAsync(Arg.Any<DeleteBlockCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidatorRejects_ReturnsFailWithoutCallingInner()
    {
        var validator = Substitute.For<IValidator<DeleteBlockCommand>>();
        validator.ValidateAsync(Arg.Any<DeleteBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("BlockId", "Block ID is required.")]));
        _serviceProvider.GetService(typeof(IValidator<DeleteBlockCommand>)).Returns(validator);

        var result = await CreateHandler().HandleAsync(new DeleteBlockCommand { BlockId = Guid.Empty });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Block ID is required.");
        await _inner.DidNotReceive().HandleAsync(Arg.Any<DeleteBlockCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidatorAccepts_CallsInnerAndReturnsItsResult()
    {
        var validator = Substitute.For<IValidator<DeleteBlockCommand>>();
        validator.ValidateAsync(Arg.Any<DeleteBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _serviceProvider.GetService(typeof(IValidator<DeleteBlockCommand>)).Returns(validator);
        var expected = DeleteBlockResult.Ok();
        _inner.HandleAsync(Arg.Any<DeleteBlockCommand>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateHandler().HandleAsync(new DeleteBlockCommand { BlockId = Guid.NewGuid() });

        result.Should().BeSameAs(expected);
    }
}
