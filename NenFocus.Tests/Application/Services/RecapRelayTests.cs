using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Domain.Enums;
using NenFocus.Application.Services;

namespace NenFocus.Tests.Application.Services;

public class RecapRelayTests
{
    [Fact]
    public void NotifyPostSessionsReady_NonEmptyList_RaisesEvent()
    {
        var relay = new RecapRelay();
        IReadOnlyList<PostSessionDto>? raisedWith = null;
        relay.PostSessionsReady += (_, postSessions) => raisedWith = postSessions;
        var missed = new PostSessionDto { SessionId = Guid.NewGuid(), Status = SessionStatus.Missed };

        relay.NotifyPostSessionsReady([missed]);

        raisedWith.Should().ContainSingle().Which.Should().Be(missed);
    }

    [Fact]
    public void NotifyPostSessionsReady_EmptyList_DoesNotRaiseEvent()
    {
        var relay = new RecapRelay();
        var raised = false;
        relay.PostSessionsReady += (_, _) => raised = true;

        relay.NotifyPostSessionsReady([]);

        raised.Should().BeFalse();
    }

    [Fact]
    public void NotifyPostContractsReady_NonEmptyList_RaisesEvent()
    {
        var relay = new RecapRelay();
        IReadOnlyList<NenContractRecapDto>? raisedWith = null;
        relay.PostContractsReady += (_, recaps) => raisedWith = recaps;
        var recap = new NenContractRecapDto { ContractId = Guid.NewGuid() };

        relay.NotifyPostContractsReady([recap]);

        raisedWith.Should().ContainSingle().Which.Should().Be(recap);
    }

    [Fact]
    public void NotifyPostContractsReady_EmptyList_DoesNotRaiseEvent()
    {
        var relay = new RecapRelay();
        var raised = false;
        relay.PostContractsReady += (_, _) => raised = true;

        relay.NotifyPostContractsReady([]);

        raised.Should().BeFalse();
    }

    [Fact]
    public void NotifyPostSessionAcknowledged_RaisesEvent()
    {
        var relay = new RecapRelay();
        var raised = false;
        relay.PostSessionAcknowledged += (_, _) => raised = true;

        relay.NotifyPostSessionAcknowledged();

        raised.Should().BeTrue();
    }
}
