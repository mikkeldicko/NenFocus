using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Services;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockValidator : AbstractValidator<DeletePlannedBlockCommand>
{
    public DeletePlannedBlockValidator(
        IPlannedBlockRepository blocks,
        INenContractRepository contracts,
        ISystemClock clock,
        IBlockChangeChecker changes)
    {
        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        // May this block be deleted? The one permission answer, BlockChangeChecker (Gap 93): a
        // running block never; a contract's block only when its contract doesn't bind it and it
        // hasn't started (a future free Bonus block) — everything else is part of the contract's
        // record, even after the contract is over.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                if (command.BlockId == Guid.Empty) return;
                var block = await blocks.GetByIdAsync(command.BlockId, ct);
                if (block is null) return;

                var contract = block.Membership is { } membership
                    ? await contracts.GetByIdAsync(membership.ContractId, ct)
                    : null;

                var verdict = changes.CheckDelete(block, contract, clock.Now);
                if (!verdict.IsAllowed)
                    context.AddFailure(nameof(DeletePlannedBlockCommand.BlockId), BlockChangeVerdictDto.From(verdict).Message);
            });
    }
}
