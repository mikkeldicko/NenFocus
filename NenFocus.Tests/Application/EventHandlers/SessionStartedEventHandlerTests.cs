using FluentAssertions;
using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

public class SessionStartedEventHandlerTests
{
    private readonly IEnforcementService _enforcement = Substitute.For<IEnforcementService>();
    private readonly IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice> _policyQuery =
        Substitute.For<IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    private SessionStartedEventHandler CreateHandler() => new(_enforcement, _policyQuery, _notifications, _clock);

    [Fact]
    public async Task HandleAsync_ComputesPolicyAndAppliesEnforcement()
    {
        var policy = new ActivePolicySlice();
        _policyQuery.HandleAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>())
            .Returns(policy);

        await CreateHandler().HandleAsync(
            new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow));

        await _enforcement.Received(1).ApplyPolicyAsync(policy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SendsSessionStartedNotification()
    {
        _policyQuery.HandleAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ActivePolicySlice());

        await CreateHandler().HandleAsync(
            new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow));

        await _notifications.Received(1).NotifyAsync(
            Arg.Is<string>(t => t.Contains("started")), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NullPolicy_SkipsApplyPolicyButStillNotifies()
    {
        _policyQuery.HandleAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>())
            .Returns((ActivePolicySlice)null!);

        await CreateHandler().HandleAsync(
            new SessionStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "Goal", DateTime.UtcNow));

        await _enforcement.DidNotReceive().ApplyPolicyAsync(Arg.Any<ActivePolicySlice>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).NotifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
