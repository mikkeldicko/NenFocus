using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed class DeleteBlockProfileCommandHandler
    : ICommandHandler<DeleteBlockProfileCommand, DeleteBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;

    public DeleteBlockProfileCommandHandler(IBlockProfileRepository profiles)
    {
        _profiles = profiles;
    }

    public async Task<DeleteBlockProfileResult> HandleAsync(
        DeleteBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        await _profiles.DeleteAsync(command.ProfileId, cancellationToken);
        return DeleteBlockProfileResult.Ok();
    }
}