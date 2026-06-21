using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed class DeleteBlockProfileCommandHandler
    : ICommandHandler<DeleteBlockProfileCommand, DeleteBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;
    private readonly IValidator<DeleteBlockProfileCommand> _validator;

    public DeleteBlockProfileCommandHandler(
        IBlockProfileRepository profiles,
        IValidator<DeleteBlockProfileCommand> validator)
    {
        _profiles = profiles;
        _validator = validator;
    }

    public async Task<DeleteBlockProfileResult> HandleAsync(
        DeleteBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return DeleteBlockProfileResult.Fail(validation.Errors.First().ErrorMessage);

        await _profiles.DeleteAsync(command.ProfileId, cancellationToken);
        return DeleteBlockProfileResult.Ok();
    }
}