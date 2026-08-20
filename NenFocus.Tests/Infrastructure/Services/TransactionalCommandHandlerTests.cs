using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.DeletePlannedBlock;
using NenFocus.Infrastructure.Services;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Services;

public class TransactionalCommandHandlerTests
{
    private readonly ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> _inner =
        Substitute.For<ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult>>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private TransactionalCommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult> CreateHandler()
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
    public async Task HandleAsync_InnerHandlerFails_StillCommitsUnitOfWork()
    {
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Fail("Something went wrong."));

        await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CommitThrowsUniqueConstraintViolation_ReturnsFailResultInstead()
    {
        // A genuine race — two commands both passed the inner handler's own
        // ExistsWithNameAsync-style check before either committed. Translated here,
        // generically, rather than in every command that touches a unique column.
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Ok());
        var sqliteEx = new SqliteException("UNIQUE constraint failed", 19, 2067);
        _uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DbUpdateException("save failed", sqliteEx)));

        var result = await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CommitThrowsUnrelatedDbUpdateException_Rethrows()
    {
        _inner.HandleAsync(Arg.Any<DeletePlannedBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(DeletePlannedBlockResult.Ok());
        _uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DbUpdateException("some other failure")));

        var act = async () => await CreateHandler().HandleAsync(new DeletePlannedBlockCommand { BlockId = Guid.NewGuid() });

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
