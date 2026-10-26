using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckBlockPresetLocked;

namespace NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;

public sealed class DeleteBlockPresetValidator : AbstractValidator<DeleteBlockPresetCommand>
{
    public DeleteBlockPresetValidator(IQueryHandler<CheckBlockPresetLockedQuery, bool> locked)
    {
        // One reason, not two: a running planned session and its own block both use the preset at once
        // (Design Review Rule 5). The rule is BlockPreset.IsLockedAt's (Gap 89 Slice D).
        RuleFor(x => x.PresetId)
            .MustAsync(async (id, ct) => !await locked.HandleAsync(new CheckBlockPresetLockedQuery(id), ct))
            .WithMessage("This preset is in use — by the running session, a block still to come or a contract that still owes time of it — and cannot be deleted.");
    }
}
