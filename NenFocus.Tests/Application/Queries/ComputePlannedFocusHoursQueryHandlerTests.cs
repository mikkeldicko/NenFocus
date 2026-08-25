using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class ComputePlannedFocusHoursQueryHandlerTests
{
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IPlannedFocusHoursCalculator _calculator = Substitute.For<IPlannedFocusHoursCalculator>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private static readonly Guid ContractId = Guid.NewGuid();

    public ComputePlannedFocusHoursQueryHandlerTests()
    {
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
    }

    private ComputePlannedFocusHoursQueryHandler CreateHandler() => new(_blocks, _calculator, _clock);

    [Fact]
    public async Task HandleAsync_NoContractId_DoesNotQueryRepository()
    {
        var draftStart = new DateTime(2026, 1, 1, 9, 0, 0);
        var draftEnd = new DateTime(2026, 1, 1, 11, 0, 0);

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(null, [(draftStart, draftEnd)]), CancellationToken.None);

        await _blocks.DidNotReceive().GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _calculator.Received(1).Calculate(Arg.Is<IEnumerable<(DateTime Start, DateTime End)>>(r =>
            r.Count() == 1 && r.First().Start == draftStart && r.First().End == draftEnd));
    }

    [Fact]
    public async Task HandleAsync_WithContractId_MergesPersistedFocusBlocksWithDrafts()
    {
        var persistedFocus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0), contractId: ContractId);
        var persistedBackground = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 1, 2, 12, 0, 0), new DateTime(2026, 1, 2, 13, 0, 0), contractId: ContractId);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[persistedFocus, persistedBackground]);

        var draftStart = new DateTime(2026, 1, 3, 9, 0, 0);
        var draftEnd = new DateTime(2026, 1, 3, 10, 0, 0);

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(ContractId, [(draftStart, draftEnd)]), CancellationToken.None);

        var focusStart = persistedFocus.Start;
        var focusEnd = persistedFocus.End;
        var backgroundStart = persistedBackground.Start;

        // Only the Focus block's range is included — Background hours never count toward the floor.
        _calculator.Received(1).Calculate(Arg.Is<IEnumerable<(DateTime Start, DateTime End)>>(r =>
            r.Count() == 2 &&
            r.Any(x => x.Start == focusStart && x.End == focusEnd) &&
            r.Any(x => x.Start == draftStart && x.End == draftEnd) &&
            !r.Any(x => x.Start == backgroundStart)));
    }

    [Fact]
    public async Task HandleAsync_ExcludeBlockId_OmitsThatPersistedBlock()
    {
        var editingBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0), contractId: ContractId);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[editingBlock]);

        var updatedStart = new DateTime(2026, 1, 2, 9, 0, 0);
        var updatedEnd = new DateTime(2026, 1, 2, 11, 0, 0);

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(ContractId, [(updatedStart, updatedEnd)], ExcludeBlockId: editingBlock.Id),
            CancellationToken.None);

        _calculator.Received(1).Calculate(Arg.Is<IEnumerable<(DateTime Start, DateTime End)>>(r =>
            r.Count() == 1 && r.First().Start == updatedStart && r.First().End == updatedEnd));
    }

    [Fact]
    public async Task HandleAsync_ConvertsRangesToLocalTime_BeforePassingToTheCalculator()
    {
        var draftStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var draftEnd = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>().AddHours(-5));

        await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(null, [(draftStart, draftEnd)]), CancellationToken.None);

        _calculator.Received(1).Calculate(Arg.Is<IEnumerable<(DateTime Start, DateTime End)>>(r =>
            r.Count() == 1 && r.First().Start == draftStart.AddHours(-5) && r.First().End == draftEnd.AddHours(-5)));
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhateverTheCalculatorComputes()
    {
        var expected = new Dictionary<DateOnly, double> { [new DateOnly(2026, 1, 1)] = 2 };
        _calculator.Calculate(Arg.Any<IEnumerable<(DateTime, DateTime)>>()).Returns(expected);

        var result = await CreateHandler().HandleAsync(
            new ComputePlannedFocusHoursQuery(null, []), CancellationToken.None);

        result.Should().BeSameAs(expected);
    }
}
