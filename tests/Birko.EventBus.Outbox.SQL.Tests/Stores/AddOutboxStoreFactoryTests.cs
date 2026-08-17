using System;
using System.IO;
using System.Linq;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.EventBus.Outbox.Extensions;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.SQL.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Birko.EventBus.Outbox.SQL.Tests.Stores;

/// <summary>
/// Covers the <c>AddOutbox(Func&lt;IServiceProvider, IOutboxStore&gt;, …)</c> overload added in
/// <c>Birko.EventBus.Outbox@bbd9389</c>.
/// </summary>
/// <remarks>
/// <para>
/// The overload exists <b>for this store</b> — its doc comment says so: "A SQL-backed store is
/// parameterised by settings and a connector type chosen from configuration, so it cannot be [activated
/// through the container]." Until this suite it was unproven against the store that motivated it, because
/// <c>Birko.EventBus.Outbox.SQL</c> was registered in nothing and had no tests (TASK-231).
/// </para>
/// <para>
/// The generic <c>AddOutbox&lt;TStore&gt;</c> genuinely cannot serve this store, and the reason is
/// concrete rather than stylistic: <c>SqlOutboxStore&lt;DB&gt;</c> has no parameterless constructor and
/// neither of its two constructors takes types the container knows — one wants <c>SqlSettings</c>, the
/// other a configured <c>AsyncDataBaseBulkStore</c>. That is what the factory overload is for.
/// </para>
/// </remarks>
public sealed class AddOutboxStoreFactoryTests : IDisposable
{
    private readonly string _dir;

    public AddOutboxStoreFactoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "birko_outbox_di_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* not the assertion */ }
    }

    private IOutboxStore BuildSqlStore(IServiceProvider _)
    {
        var inner = new AsyncSQLiteStore<OutboxEntryModel>();
        inner.SetSettings(new SqLiteSettings(_dir, "outbox.db"));
        return new SqlOutboxStore<SqLiteConnector>(inner);
    }

    [Fact]
    public void The_factory_overload_resolves_a_SqlOutboxStore_as_IOutboxStore()
    {
        var services = new ServiceCollection();
        services.AddOutbox(BuildSqlStore);

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IOutboxStore>();

        store.Should().BeOfType<SqlOutboxStore<SqLiteConnector>>(
            "the factory's return value is what the container must hand out");
    }

    /// <summary>
    /// The processor publishes what it drains, so it takes an <see cref="IEventBus"/>. The outbox
    /// registration deliberately does not supply one — that is the host's choice of transport — so a test
    /// that wants to *construct* the processor has to provide it.
    /// </summary>
    private sealed class NoOpEventBus : IEventBus
    {
        public System.Threading.Tasks.Task PublishAsync<TEvent>(TEvent @event, System.Threading.CancellationToken cancellationToken = default)
            where TEvent : IEvent
            => System.Threading.Tasks.Task.CompletedTask;

        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
            => throw new NotSupportedException("the outbox processor only publishes");

        public void Dispose() { }
    }

    [Fact]
    public void The_factory_overload_registers_the_processor_and_its_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventBus, NoOpEventBus>();
        services.AddOutbox(BuildSqlStore);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<OutboxProcessor>().Should().NotBeNull(
            "without this the caller has to re-implement the processor registration by hand, which is the " +
            "duplication the overload was added to remove");
        sp.GetServices<IHostedService>().Should().NotBeEmpty();
    }

    [Fact]
    public void The_processor_is_registered_but_needs_the_host_to_supply_an_event_bus()
    {
        var services = new ServiceCollection();
        services.AddOutbox(BuildSqlStore);   // deliberately no IEventBus

        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<OutboxProcessor>();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*IEventBus*",
               "the outbox does not choose a transport, so the descriptor registers and resolution is " +
               "what fails — pinned so a future change that silently registers a default bus is noticed");
    }

    [Fact]
    public void The_factory_overload_honours_the_options_callback()
    {
        var services = new ServiceCollection();
        services.AddOutbox(BuildSqlStore, o => o.BatchSize = 7);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<OutboxOptions>().BatchSize.Should().Be(7);
    }

    [Fact]
    public void The_factory_overload_defaults_its_options_when_no_callback_is_given()
    {
        var services = new ServiceCollection();
        services.AddOutbox(BuildSqlStore);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<OutboxOptions>().Should().NotBeNull(
            "the options object is registered unconditionally, so the processor can always resolve it");
    }

    [Fact]
    public void A_null_factory_is_rejected_at_registration_rather_than_at_resolution()
    {
        var services = new ServiceCollection();

        var act = () => services.AddOutbox((Func<IServiceProvider, IOutboxStore>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("storeFactory",
            "failing at registration names the mistake; failing later at resolution would surface far " +
            "from its cause");
    }

    [Fact]
    public async System.Threading.Tasks.Task The_resolved_store_actually_works_end_to_end()
    {
        var services = new ServiceCollection();
        services.AddOutbox(BuildSqlStore);
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IOutboxStore>();
        var entry = new OutboxEntry
        {
            EventId = Guid.NewGuid(), EventType = "ViaDi", Payload = "{}", Source = "tests",
        };

        await store.SaveAsync(entry);
        var pending = await store.GetPendingAsync(10);

        pending.Should().ContainSingle(e => e.Id == entry.Id,
            "resolving the type is not the same as the wiring producing a working store");
    }
}
