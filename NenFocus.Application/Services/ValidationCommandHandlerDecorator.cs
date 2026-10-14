using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Services;

public sealed class ValidationCommandHandlerDecorator<TCommand, TResult>
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : ICommandResult<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IServiceProvider _serviceProvider;

    public ValidationCommandHandlerDecorator(
        ICommandHandler<TCommand, TResult> inner,
        IServiceProvider serviceProvider)
    {
        _inner = inner;
        _serviceProvider = serviceProvider;
    }

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var validator = _serviceProvider.GetService<IValidator<TCommand>>();
        if (validator is not null)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
                return TResult.Fail(validation.Errors[0].ErrorMessage);
        }

        return await _inner.HandleAsync(command, cancellationToken);
    }
}
