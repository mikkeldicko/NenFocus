using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Events;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Services;

namespace NenFocus.Tests.Infrastructure.Services;

/// <summary>
/// Events are published after the commit, each handler in a scope of its own, so nothing would
/// save what a handler changes — the change would vanish silently. A reaction that changes state
/// dispatches a command, which saves in its own transaction (Vernon: one aggregate per
/// transaction). The publisher refuses a handler that leaves unsaved changes behind (Gap 110).
/// </summary>
public sealed class EventHandlersSaveNothingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<NenFocusDbContext> _options;
    private readonly FocusBlock _block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(1), Now.AddHours(2)));

    public EventHandlersSaveNothingTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(_connection).Options;
        using var context = new NenFocusDbContext(_options);
        context.Database.EnsureCreated();
        context.Add(_block);
        context.SaveChanges();
    }

    /// <summary>Loads the stored block through the scope's context and either cancels it or only reads it.</summary>
    private sealed class BlockHandler(NenFocusDbContext context, bool changesIt) : IDomainEventHandler<SessionEndedEvent>
    {
        public static Guid BlockId;

        public async Task HandleAsync(SessionEndedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            var block = await context.PlannedBlocks.SingleAsync(b => b.Id == BlockId, cancellationToken);
            if (changesIt) block.MarkCancelled(At.Utc(Now));
        }
    }

    private Task PublishWithHandlerThat(bool changesIt)
    {
        BlockHandler.BlockId = _block.Id;
        var services = new ServiceCollection();
        services.AddScoped(_ => new NenFocusDbContext(_options));
        services.AddScoped<IDomainEventHandler<SessionEndedEvent>>(sp => new BlockHandler(sp.GetRequiredService<NenFocusDbContext>(), changesIt));
        var publisher = new InProcessDomainEventPublisher(services.BuildServiceProvider());

        return publisher.PublishAsync(new SessionEndedEvent(Guid.NewGuid(), At.Utc(Now)));
    }

    [Fact]
    public async Task AHandlerThatChangesAnAggregate_IsRefused_NamingWhatItChanged()
    {
        var act = () => PublishWithHandlerThat(changesIt: true);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(FocusBlock)).And.Contain("dispatch a command");
    }

    [Fact]
    public async Task AHandlerThatOnlyReads_IsFine()
    {
        var act = () => PublishWithHandlerThat(changesIt: false);

        await act.Should().NotThrowAsync();
    }

    public void Dispose() => _connection.Dispose();
}
