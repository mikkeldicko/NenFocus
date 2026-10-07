using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Only the contract decides what may happen to it at each stage of its life (Gap 92, D): edit or
/// delete only while Scheduled, complete only while Active, break only while in effect. Each is one
/// verdict on <c>NenContract</c> (<c>CheckEdit</c>, <c>CheckDelete</c>, <c>CheckComplete</c>,
/// <c>CheckBreak</c>), and the actions obey their own. So no Application code compares a contract's
/// status to decide something — that comparison is how "only Scheduled" came to be written in a
/// validator and a handler, and "only Active" in a validator, a query and the Planner. A text scan:
/// it sees comparisons with a status value, not every way a decision could be dressed up.
/// </summary>
public class ContractLifecycleRuleTests
{
    // contract.Status == / != NenContractStatus.X, or a pattern { Status: NenContractStatus.X }.
    private static readonly Regex StatusDecision =
        new(@"Status\s*(==|!=)\s*NenContractStatus\.|Status\s*:\s*NenContractStatus\.");

    [Fact]
    public void NoApplicationCodeDecidesFromAContractsStatus()
    {
        var violations = SourceCode.Files("NenFocus.Application")
            .Select(f => (f.Path, Count: StatusDecision.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, KnownStatusReads);
    }

    private static readonly string[] KnownStatusReads =
    [
        // Display order, not a decision: active contracts listed first.
        "NenFocus.Application/Queries/GetInEffectNenContracts/GetInEffectNenContractsQueryHandler.cs has 1",
        "NenFocus.Application/Queries/GetNenContractsForDateRange/GetNenContractsForDateRangeQueryHandler.cs has 1",
        // → Gap 103: "has finished" (Completed or Broken) becomes one question on the contract.
        "NenFocus.Application/UseCases/NenContracts/AcknowledgePostContract/AcknowledgePostContractCommandHandler.cs has 2",
        "NenFocus.Application/UseCases/NenContracts/SaveReflection/SaveNenContractReflectionCommandHandler.cs has 2",
    ];
}
