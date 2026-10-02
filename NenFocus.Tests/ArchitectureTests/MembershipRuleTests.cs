using System.Text.RegularExpressions;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Only the Domain decides contract membership (Gap 97): which contract a block belongs to and how
/// (Committed, Redemption, Bonus) is stored on the block and answered by named questions —
/// <c>CountsTowardFloor</c>, <c>IsBonus</c>, <c>IsRedemption</c>, <c>PlaceUnder</c>,
/// <c>NenContract.Governs</c>. The role itself is Domain-only, so the compiler guards that half.
/// The contract id stays readable, because handlers need it to load the contract — so this test
/// guards the other half: outside the Domain and persistence, nobody *compares* a block's contract
/// id (is it null, is it this contract) to decide something. That comparison is how "Bonus = no
/// contract id" came to be written in four places. A text scan: it sees comparisons, not every way
/// a decision could be dressed up — review covers the rest.
/// </summary>
public class MembershipRuleTests
{
    // block.Membership?.ContractId compared: == / != / is null / is not null / is not { }.
    private static readonly Regex MembershipDecision =
        new(@"Membership\s*[?!]?\s*\.\s*ContractId\s*(==|!=|is\s+null\b|is\s+not\s+(null\b|\{))");

    // The same decision made in the UI on the block DTO's contract id.
    private static readonly Regex DtoDecision = new(@"Block\s*[?!]?\s*\.\s*ContractId\s+is\s+(not\s+)?null\b");

    private static readonly string[] KnownDecisions =
    [
        // Gap 68: Edit leaves the contract's own old blocks out of its overlap check, since the
        // re-draft replaces them; adoption rewrites that exclusion.
        "NenFocus.Application/UseCases/NenContracts/Edit/EditNenContractCommandHandler.cs has 1",
    ];

    [Fact]
    public void OnlyTheDomainDecidesMembershipFromABlocksContractId()
    {
        var violations = SourceCode.Files("NenFocus.Application", "NenFocus")
            .Select(f => (f.Path, Count: MembershipDecision.Matches(f.Code).Count + DtoDecision.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, KnownDecisions);
    }
}
