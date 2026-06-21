using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed class CreateBlockProfileCommandHandler
    : ICommandHandler<CreateBlockProfileCommand, CreateBlockProfileResult>
{
    private readonly IBlockProfileRepository _profiles;
    private readonly IValidator<CreateBlockProfileCommand> _validator;

    public CreateBlockProfileCommandHandler(
        IBlockProfileRepository profiles,
        IValidator<CreateBlockProfileCommand> validator)
    {
        _profiles = profiles;
        _validator = validator;
    }

    public async Task<CreateBlockProfileResult> HandleAsync(
        CreateBlockProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Safety Shield Validation
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return CreateBlockProfileResult.Fail(validationResult.Errors.First().ErrorMessage);
        }

        // 2. Business Logic Check
        if (await _profiles.ExistsWithNameAsync(command.Name, cancellationToken: cancellationToken))
            return CreateBlockProfileResult.Fail($"A profile named '{command.Name}' already exists.");

        // 3. Create Domain Aggregate (using your factory method)
        var profile = BlockProfile.Create(Guid.NewGuid(), command.Name, command.WebsitePolicy, DateTime.UtcNow, command.Tags.ToList());

        foreach (var appName in command.BlockedApps)
            profile.AddBlockedApp(new BlockedApp(appName));

        foreach (var url in command.Websites)
            profile.AddWebsite(new BlockedSite(url));

        await _profiles.AddAsync(profile, cancellationToken);

        return CreateBlockProfileResult.Ok(profile.Id);
    }
}