using NenFocus.Application.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed class DeleteBlockProfileCommandHandler
    : ICommandHandler<DeleteBlockProfileCommand>
{
    private readonly IBlockProfileRepository _profiles;

    public DeleteBlockProfileCommandHandler(IBlockProfileRepository profiles)
        => _profiles = profiles;

    public async Task<bool> HandleAsync(
        DeleteBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        await _profiles.DeleteAsync(command.ProfileId, cancellationToken);
        return true;
    }
}