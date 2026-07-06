using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;

namespace NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;

public sealed class UpdateBlockPresetValidator : AbstractValidator<UpdateBlockPresetCommand>
{
    public UpdateBlockPresetValidator(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks)
    {
        RuleFor(x => x.PresetId)
            .MustAsync(async (id, ct) =>
            {
                var active = await sessions.GetActiveAsync(ct);
                return active?.BlockPresetId != id;
            })
            .WithMessage("This preset is in use by an active session and cannot be modified.");

        RuleFor(x => x.PresetId)
            .MustAsync(async (id, ct) =>
                !await plannedBlocks.HasFutureBlocksForPresetAsync(id, DateTime.UtcNow, ct))
            .WithMessage("This preset is assigned to one or more future planned blocks and cannot be modified.");

        RuleForEach(x => x.BlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(ProcessUtils.NormalizeProcessName(app)))
            .WithMessage((cmd, app) => $"'{app}' is a critical system process and cannot be blocked.");
    }
}
