using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.UseCases.Planner.CoverDeficit;

/// <summary>
/// Asks the contract whether the tile may pay its debt there (<c>CheckRedemptionPlacement</c>),
/// then the placement rulebook on the slot that will be saved — the capped one, with the
/// Redemption membership the block will hold (Gap 106, decision 13; Design Review Rule 12).
/// </summary>
public sealed class CoverDeficitValidator : AbstractValidator<CoverDeficitCommand>
{
    public CoverDeficitValidator(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        BlockPlacementChecker placement,
        ISystemClock clock)
    {
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                if (contract is null)
                {
                    context.AddFailure(nameof(CoverDeficitCommand.ContractId), RedemptionVerdictDto.ContractNotFound);
                    return;
                }

                var now = clock.Now;
                var redemption = contract.CheckRedemptionPlacement(
                    command.PresetId, command.Slot, await blocks.GetForContractAsync(contract.Id, ct), now, clock.TimeZone);
                if (redemption.Outcome != RedemptionPlacementOutcome.Placed)
                {
                    context.AddFailure(nameof(CoverDeficitCommand.Slot), RedemptionVerdictDto.From(redemption.Outcome).Message!);
                    return;
                }

                var slot = redemption.Slot!;
                var verdict = placement.Check(slot, BlockPresetType.Focus, redemption.Membership,
                    await contracts.GetAllInEffectAsync(ct),
                    await blocks.GetForRangeAsync(slot, ct),
                    await sessions.GetActiveAsync(ct),
                    now, clock.TimeZone);
                if (verdict.Outcome != PlacementOutcome.Allowed)
                    context.AddFailure(nameof(CoverDeficitCommand.Slot), BlockPlacementVerdictDto.From(verdict).Message!);
            });
    }
}
