using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// A refusal is a typed reason, not a sentence (MODULE_STANDARDS → Vertical Slice Structure, Gap 100):
/// a validator says *what* went wrong; its wording lives in one place — today a topic's wording home in
/// Application (e.g. BlockPlacementVerdictDto.From, Notifier), after Gap 100 a resource file. Counts every
/// WithMessage(…)/AddFailure(…) that writes a string literal, per validator file. The list may only
/// shrink: no new validator writes a sentence, no listed one writes more. Gap 93 converted the
/// permission validators' rules (worded in BlockChangeVerdictDto.From); Gap 100 (step R1: validator
/// text into Refusals.resx) empties the list, and the rule becomes "zero". Passing a sentence on from a wording home (verdict.Message) doesn't count.
/// A text scan: it can't tell a proper wording home from a sentence hidden in a helper — review holds that line.
/// </summary>
public class ValidatorWordingRuleTests
{
    private static readonly Regex SentenceCall = new(@"\b(WithMessage|AddFailure)\s*\([^;]*?""");

    private static readonly string[] KnownSentenceWriters =
    [
        // → Gap 100: what Gap 93 left in the permission validators — input shape only (required
        // ids, a split's pieces), their rules now come from BlockChangeChecker as typed reasons.
        "NenFocus.Application/UseCases/Planner/DeletePlannedBlock/DeletePlannedBlockValidator.cs has 1",
        "NenFocus.Application/UseCases/Planner/SplitPlannedBlock/SplitPlannedBlockValidator.cs has 7",
        "NenFocus.Application/UseCases/Planner/UpdatePlannedBlock/UpdatePlannedBlockValidator.cs has 4",

        // → Gap 100 step R1: every remaining sentence into Refusals.resx.
        "NenFocus.Application/UseCases/BlockPresets/CreateBlockPreset/CreateBlockPresetValidator.cs has 2",
        "NenFocus.Application/UseCases/BlockPresets/DeleteBlockPreset/DeleteBlockPresetValidator.cs has 2",
        "NenFocus.Application/UseCases/BlockPresets/UpdateBlockPreset/UpdateBlockPresetValidator.cs has 1",
        "NenFocus.Application/UseCases/ContractPresets/CreateContractPreset/CreateContractPresetValidator.cs has 1",
        "NenFocus.Application/UseCases/ContractPresets/DeleteContractPreset/DeleteContractPresetValidator.cs has 1",
        "NenFocus.Application/UseCases/ContractPresets/UpdateContractPreset/UpdateContractPresetValidator.cs has 2",
        "NenFocus.Application/UseCases/NenContracts/Break/BreakNenContractValidator.cs has 2",
        "NenFocus.Application/UseCases/NenContracts/CompleteNenContract/CompleteNenContractValidator.cs has 1",
        "NenFocus.Application/UseCases/NenContracts/Delete/DeleteNenContractValidator.cs has 1",
        "NenFocus.Application/UseCases/NenContracts/Edit/EditNenContractValidator.cs has 7",
        "NenFocus.Application/UseCases/NenContracts/Seal/SealNenContractValidator.cs has 5",
        "NenFocus.Application/UseCases/Planner/CreatePlannedBlock/CreatePlannedBlockValidator.cs has 4",
        "NenFocus.Application/UseCases/Sessions/MarkFocusBlockMissed/MarkFocusBlockMissedValidator.cs has 4",
        "NenFocus.Application/UseCases/Sessions/StartSession/StartSessionValidator.cs has 4",
    ];

    [Fact]
    public void ValidatorsDoNotWriteNewSentences()
    {
        var violations = SourceCode.Files("NenFocus.Application")
            .Where(f => f.Path.EndsWith("Validator.cs", StringComparison.Ordinal))
            .Select(f => (f.Path, Count: SentenceCall.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, KnownSentenceWriters);
    }
}
