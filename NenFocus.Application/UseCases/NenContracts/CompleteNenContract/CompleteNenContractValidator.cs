using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed class CompleteNenContractValidator : AbstractValidator<CompleteNenContractCommand>
{
    public CompleteNenContractValidator(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        ISystemClock clock)
    {
        RuleFor(x => x.ContractId)
            .MustAsync(async (id, ct) =>
            {
                var contract = await contracts.GetByIdAsync(id, ct);
                return contract is not null;
            })
            .WithMessage("Contract not found.");

        RuleFor(x => x.ContractId)
            .MustAsync(async (id, ct) =>
            {
                var contract = await contracts.GetByIdAsync(id, ct);
                return contract is null || contract.Status == NenContractStatus.Active;
            })
            .WithMessage("Only an active contract can be completed.");

        // The real gate: every committed hour delivered, and nothing left pending — no
        // still-open block (committed, redemption, or Bonus) anywhere in the contract's
        // own window. The second half isn't just conservative: it's what guarantees
        // completing here never needs Break's cleanup logic, because by construction
        // there's nothing left to clean up.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                if (contract is null || contract.Status != NenContractStatus.Active) return;

                var completedByDay = await completedHours.HandleAsync(
                    new GetNenContractCompletedFocusHoursQuery(contract.Id), ct);
                var totalCompleted = completedByDay.Values.Sum();

                if (totalCompleted < contract.CommittedFocusHours)
                {
                    context.AddFailure(nameof(CompleteNenContractCommand.ContractId),
                        $"You haven't completed all {contract.CommittedFocusHours:0.#}h committed yet ({totalCompleted:0.#}h so far).");
                    return;
                }

                var now = clock.UtcNow;
                var rangeStart = contract.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
                var rangeEnd = contract.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
                var blocksInRange = await blocks.GetForRangeAsync(rangeStart, rangeEnd, ct);

                var stillPending = blocksInRange.Any(b =>
                    !b.CancelledAt.HasValue &&
                    !b.HasEnded(now) &&
                    (b.ContractId == contract.Id ||
                        (b.ContractId is null &&
                         DateOnly.FromDateTime(b.Start) >= contract.StartDate &&
                         DateOnly.FromDateTime(b.Start) <= contract.EndDate)));

                if (stillPending)
                    context.AddFailure(nameof(CompleteNenContractCommand.ContractId),
                        "There's still a block scheduled in this contract's window — move or remove it before completing.");
            });
    }
}
