using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetActiveSession;
using NenFocus.Domain.Enums;
using NenFocus.Application.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class ActiveSessionStateTests
{
    private readonly IQueryDispatcher _queries = Substitute.For<IQueryDispatcher>();

    private ActiveSessionState CreateState() => new(_queries);

    private void DatabaseHolds(SessionDto? running) =>
        _queries.QueryAsync(Arg.Any<GetActiveSessionQuery>(), Arg.Any<CancellationToken>()).Returns(running);

    [Fact]
    public async Task RefreshAsync_SessionRunning_ReadsItFromTheQueryAndRaisesSessionChanged()
    {
        var running = new SessionDto { Id = Guid.NewGuid(), Status = SessionStatus.Active };
        DatabaseHolds(running);
        var state = CreateState();
        SessionDto? raisedWith = null;
        state.SessionChanged += (_, dto) => raisedWith = dto;

        await state.RefreshAsync();

        state.ActiveSession.Should().Be(running);
        raisedWith.Should().Be(running);
    }

    [Fact]
    public async Task RefreshAsync_SessionEnded_ClearsItAndRaisesSessionChangedWithNull()
    {
        DatabaseHolds(new SessionDto { Id = Guid.NewGuid(), Status = SessionStatus.Active });
        var state = CreateState();
        await state.RefreshAsync();
        DatabaseHolds(null);
        var raisedWith = new SessionDto(); // sentinel, overwritten with null
        state.SessionChanged += (_, dto) => raisedWith = dto;

        await state.RefreshAsync();

        state.ActiveSession.Should().BeNull();
        raisedWith.Should().BeNull();
    }

    [Fact]
    public void BeforeAnyRefresh_NoSessionIsKnown()
    {
        CreateState().ActiveSession.Should().BeNull();
    }
}
