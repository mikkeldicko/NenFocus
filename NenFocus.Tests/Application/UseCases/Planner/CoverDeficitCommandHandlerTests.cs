using FluentAssertions;
using NenFocus.Application.UseCases.Planner.CoverDeficit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// The validator has already allowed the placement; the handler asks the contract to build the
/// block (<c>PlaceRedemptionBlock</c>) and saves it — one block, one transaction (Gap 106).
/// </summary>
public class CoverDeficitCommandHandlerTests
{
    private static readonly DateTime Now = CoverDeficitTestContext.Now;
    private readonly CoverDeficitTestContext _context = new();

    private CoverDeficitCommandHandler CreateHandler() => new(_context.Contracts, _context.Blocks, _context.Clock);

    private CoverDeficitCommand Command(DateTime start, DateTime end) => new()
    {
        ContractId = _context.Contract.Id,
        PresetId = _context.PresetId,
        Slot = At.Slot(start, end)
    };

    private async Task<(CoverDeficitResult Result, FocusBlock Saved)> HandleAsync(CoverDeficitCommand command)
    {
        PlannedBlock? saved = null;
        await _context.Blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(command);

        return (result, saved.Should().BeOfType<FocusBlock>().Subject);
    }

    [Fact]
    public async Task HandleAsync_SavesARedemptionBlockOfTheContract_HoldingItsReceipt()
    {
        var (result, saved) = await HandleAsync(Command(Now.AddHours(1), Now.AddHours(2)));

        result.Success.Should().BeTrue();
        result.BlockId.Should().Be(saved.Id);
        result.WasCapped.Should().BeFalse();
        saved.IsRedemption.Should().BeTrue();
        saved.Membership!.ContractId.Should().Be(_context.Contract.Id);
        saved.BlockPresetId.Should().Be(_context.PresetId);
        saved.Scheduled.Should().Be(At.Slot(Now.AddHours(1), Now.AddHours(2)));
        saved.Receipt.Entries.Should().Equal(new RedemptionEntry(_context.Missed.Id, WholeMinutes.Of(60)));
    }

    [Fact]
    public async Task HandleAsync_LongerThanTheDebt_SavesTheCappedSlotAndSaysSo()
    {
        var (result, saved) = await HandleAsync(Command(Now.AddHours(1), Now.AddHours(3)));

        result.WasCapped.Should().BeTrue();
        saved.Scheduled.Should().Be(At.Slot(Now.AddHours(1), Now.AddHours(2)));
    }
}
