using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;
using System;
using System.IO;

namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed class UpdateBlockProfileValidator : AbstractValidator<UpdateBlockProfileCommand>
{
    public UpdateBlockProfileValidator(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks)
    {
        RuleFor(x => x.ProfileId)
            .MustAsync(async (id, ct) =>
            {
                var active = await sessions.GetActiveAsync(ct);
                return active?.BlockProfileId != id;
            })
            .WithMessage("This profile is in use by an active session and cannot be modified.");

        RuleFor(x => x.ProfileId)
            .MustAsync(async (id, ct) =>
                !await plannedBlocks.HasFutureBlocksForProfileAsync(id, DateTime.UtcNow, ct))
            .WithMessage("This profile is assigned to one or more future planned blocks and cannot be modified.");

        RuleForEach(x => x.BlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(Normalize(app)))
            .WithMessage((cmd, app) => $"'{app}' is a critical system process and cannot be blocked.");
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        try
        {
            string fileName = Path.GetFileName(input);
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^4];
            return fileName.Trim().ToLowerInvariant();
        }
        catch { return input.Trim().ToLowerInvariant(); }
    }
}