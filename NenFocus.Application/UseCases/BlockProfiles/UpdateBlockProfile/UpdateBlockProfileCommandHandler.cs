using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed class UpdateBlockProfileCommandHandler
    : ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;

    public UpdateBlockProfileCommandHandler(IBlockProfileRepository profiles)
    {
        _profiles = profiles;
    }

    public async Task<UpdateBlockProfileResult> HandleAsync(
        UpdateBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return UpdateBlockProfileResult.Fail($"Profile '{command.ProfileId}' not found.");

        if (await _profiles.ExistsWithNameAsync(command.Name, excludeId: command.ProfileId, cancellationToken: cancellationToken))
            return UpdateBlockProfileResult.Fail($"A profile named '{command.Name}' already exists.");

        profile.Rename(command.Name);
        profile.ChangeWebsitePolicy(command.WebsitePolicy);
        if (command.ProfileType != profile.ProfileType)
            profile.ChangeProfileType(command.ProfileType);

        foreach (var existing in profile.BlockedApps.ToList()) profile.RemoveBlockedApp(existing);
        foreach (var appName in command.BlockedApps) profile.AddBlockedApp(new BlockedApp(appName));

        foreach (var existing in profile.Websites.ToList()) profile.RemoveWebsite(existing);
        foreach (var url in command.Websites) profile.AddWebsite(new BlockedSite(url));

        foreach (var existing in profile.Tags.ToList()) profile.RemoveTag(existing);
        foreach (var tag in command.Tags) profile.AddTag(tag);

        await _profiles.UpdateAsync(profile, cancellationToken);
        return UpdateBlockProfileResult.Ok();
    }
}
