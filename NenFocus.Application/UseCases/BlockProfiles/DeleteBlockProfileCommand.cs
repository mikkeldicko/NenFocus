using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

// ── Command ────────────────────────────────────────────────────────────────

public sealed record DeleteBlockProfileCommand
{
    public Guid ProfileId { get; init; }
}

// ── Handler ────────────────────────────────────────────────────────────────

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
