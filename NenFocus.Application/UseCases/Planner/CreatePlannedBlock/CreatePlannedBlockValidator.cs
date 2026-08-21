using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockValidator : AbstractValidator<CreatePlannedBlockCommand>
{
    public CreatePlannedBlockValidator(
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        ISystemClock clock,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution)
    {
        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.PresetId == Guid.Empty) return true;
                var preset = await presets.GetByIdAsync(command.PresetId, ct);
                return preset is null || preset.PresetType == command.BlockPresetType;
            })
            .WithMessage(x =>
                $"A {x.BlockPresetType.ToString().ToLowerInvariant()} block requires a {x.BlockPresetType.ToString().ToLowerInvariant()} preset.");

        RuleFor(x => x.Start)
            .Must(start => start > clock.UtcNow)
            .WithMessage("Start time must be in the future.");

        RuleFor(x => x.End)
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.")
            .Must((command, end) => (end - command.Start).TotalMinutes >= 1)
            .WithMessage("Block must be at least 1 minute long.");

        // Focus blocks: the contract that actually applies may not be command.ContractId
        // itself — a null ContractId still auto-discovers a contract via
        // ComputeRedemptionAttributionQuery when the block turns out to cover a deficit.
        // Resolving through the same query the handler uses (rather than checking
        // command.ContractId directly) keeps Work Hours validation in sync with whichever
        // contract the block actually ends up attached to.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var contractId = command.BlockPresetType == BlockPresetType.Focus
                    ? (await computeRedemptionAttribution.HandleAsync(
                        new ComputeRedemptionAttributionQuery(command.ContractId, command.PresetId, command.Start, command.End, ExcludeBlockId: null),
                        ct)).ContractId
                    : command.ContractId;

                if (contractId is not { } resolvedContractId) return true;
                var contract = await contracts.GetByIdAsync(resolvedContractId, ct);
                return contract is null
                    || contract.AllowsBlockPlacement(DateOnly.FromDateTime(command.Start), command.Start, command.End);
            })
            .WithMessage("This block falls outside the contract's configured Work Hours for that day.");
    }
}
