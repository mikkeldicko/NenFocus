using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Application.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class TransactionCommandHandlerDecoratorTests
{
    private readonly ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> _inner =
        Substitute.For<ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult>>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private TransactionCommandHandlerDecorator<DeletePlannedBlockCommand, DeletePlannedBlockResult> CreateHandler()
        => new(_inner, _uow);

    [Fact]
    public async Task HandleAsync_CallsInnerHandlerThenCommitsUnitOfWork()
    {
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Ok());

        await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        Received.InOrder(() =>
        {
            _inner.Received().HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>());
            _uow.Received().CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_ReturnsInnerHandlerResult()
    {
        var expected = DeletePlannedBlockResult.Ok();
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task HandleAsync_InnerHandlerFails_DiscardsItsChangesInsteadOfCommitting()
    {
        // Gap 110: a failed command saves nothing — the rule lives here once, not in every
        // handler's habit of checking everything before changing anything.
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Fail("Something went wrong."));

        await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        _uow.Received(1).Discard();
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ResultThatCannotFail_AlwaysCommits()
    {
        // The sweeps' results carry no Success flag — they never fail, so they always commit.
        var inner = Substitute.For<ICommandHandler<SweepNenContractLifecycleCommand, SweepNenContractLifecycleResult>>();
        inner.HandleAsync(Arg.Any<SweepNenContractLifecycleCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SweepNenContractLifecycleResult());

        await new TransactionCommandHandlerDecorator<SweepNenContractLifecycleCommand, SweepNenContractLifecycleResult>(inner, _uow)
            .HandleAsync(new SweepNenContractLifecycleCommand { AsOf = At.Utc(2026, 1, 5) });

        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        _uow.DidNotReceive().Discard();
    }

    [Fact]
    public async Task HandleAsync_CommitThrowsUniqueConstraintViolation_Propagates()
    {
        // Gap 112 (I1): unique indexes are backstops — validators own "the name is taken", the
        // Domain owns "one running session". A backstop that fires is a bug, so it stays loud
        // instead of being reworded (it used to become "That name is already in use." for every
        // index, the session ones included).
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Ok());
        var sqliteEx = new SqliteException("UNIQUE constraint failed", 19, 2067);
        _uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DbUpdateException("save failed", sqliteEx)));

        var act = async () => await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
