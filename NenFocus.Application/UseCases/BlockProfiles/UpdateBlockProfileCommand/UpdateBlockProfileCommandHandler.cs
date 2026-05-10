using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed class UpdateBlockProfileCommandHandler
    : ICommandHandler<UpdateBlockProfileCommand, UpdateBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;
    private readonly IValidator<UpdateBlockProfileCommand> _validator;

    public UpdateBlockProfileCommandHandler(
        IBlockProfileRepository profiles,
        IValidator<UpdateBlockProfileCommand> validator)
    {
        _profiles = profiles;
        _validator = validator;
    }

    public async Task<UpdateBlockProfileResult> HandleAsync(
        UpdateBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return UpdateBlockProfileResult.Fail(validationResult.Errors.First().ErrorMessage);

        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return UpdateBlockProfileResult.Fail($"Profile '{command.ProfileId}' not found.");

        // Fix the call to skip Guid mismatch
        if (await _profiles.ExistsWithNameAsync(command.Name, excludeId: command.ProfileId, cancellationToken: cancellationToken))
            return UpdateBlockProfileResult.Fail($"A profile named '{command.Name}' already exists.");

        profile.Rename(command.Name);
        profile.ChangeWebsitePolicy(command.WebsitePolicy);

        // Apps
        foreach (var existing in profile.BlockedApps.ToList()) profile.RemoveBlockedApp(existing);
        foreach (var appName in command.BlockedApps) profile.AddBlockedApp(new BlockedApp(appName));

        // Websites
        foreach (var existing in profile.Websites.ToList()) profile.RemoveWebsite(existing);
        foreach (var url in command.Websites) profile.AddWebsite(new BlockedSite(url));

        // Tags
        foreach (var existing in profile.Tags.ToList()) profile.RemoveTag(existing);
        foreach (var tag in command.Tags) profile.AddTag(tag);

        await _profiles.UpdateAsync(profile, cancellationToken);
        return UpdateBlockProfileResult.Ok();
    }
}