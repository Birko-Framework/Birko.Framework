using Birko.Configuration;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Birko.Data.EventSourcing.Tests;

/// <summary>TASK-995 — the event store contract on the default store over an InMemory factory.</summary>
public sealed class InMemoryEventStoreContractTests : EventStoreContract
{
    // One factory: an InMemory factory answers the same store for a type every time, so each GetEventStore() is a new
    // writer over the same data.
    private readonly IStoreFactory _factory = Birko.Data.Stores.StoreFactory.Create("InMemory", null, p => p.AddInMemory());

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult(_factory.GetEventStore());
}

/// <summary>TASK-995 — the record id, the default store's compensation, and how <c>GetEventStore</c> picks a store.</summary>
public sealed class GenericEventStoreTests
{
    [Fact]
    public void The_name_based_id_is_RFC_9562_version_5()
    {
        // RFC 9562 / RFC 4122 example: uuid5(NAMESPACE_DNS, "www.example.com").
        EventRecord.NameBasedV5(new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), "www.example.com")
            .Should().Be(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"));
    }

    [Fact]
    public void A_record_id_is_the_same_for_one_aggregate_version_and_differs_otherwise()
    {
        var a = Guid.NewGuid();
        EventRecord.IdFor(a, 1).Should().Be(EventRecord.IdFor(a, 1));
        EventRecord.IdFor(a, 1).Should().NotBe(EventRecord.IdFor(a, 2));
        EventRecord.IdFor(a, 1).Should().NotBe(EventRecord.IdFor(Guid.NewGuid(), 1));
        EventRecord.FromEvent(new DomainEvent(a, 7, "T", "{}")).Guid.Should().Be(EventRecord.IdFor(a, 7));
    }

    private sealed class FailingDeleteStore : AsyncInMemoryStore<EventRecord>
    {
        protected override Task DeleteCoreAsync(EventRecord data, CancellationToken ct = default)
            => throw new IOException("delete failed");

        protected override Task DeleteCoreAsync(IEnumerable<EventRecord> data, CancellationToken ct = default)
            => throw new IOException("delete failed");
    }

    [Fact]
    public async Task A_batch_whose_compensation_fails_says_which_events_remain()
    {
        var store = new FailingDeleteStore();
        store.SetSettings(new Settings("t995", "t995"));
        var events = new AsyncStoreEventStore(store);
        var a = Guid.NewGuid();
        await events.AppendAsync(new DomainEvent(a, 3, "Taken", "{}"));

        var act = () => events.AppendRangeAsync(new IEvent[] { new DomainEvent(a, 1, "T", "{}"), new DomainEvent(a, 2, "T", "{}"), new DomainEvent(a, 3, "T", "{}") });
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Message.Should().Contain($"{a}@1").And.Contain($"{a}@2");
        var both = thrown.InnerException.Should().BeOfType<AggregateException>().Which.InnerExceptions;
        both[0].Should().BeOfType<Birko.Data.Exceptions.StoreUniqueViolationException>();
        both[1].Should().BeOfType<IOException>();
    }

    private sealed class FakeFactory : IStoreFactory
    {
        public FakeFactory(string backend) => Backend = backend;
        public string Backend { get; }
        public ISettings Settings { get; } = new Settings("fake", "fake");
        public IAsyncBulkStore<T> GetAsyncStore<T>() where T : AbstractModel
        {
            var store = new AsyncInMemoryStore<T>();
            store.SetSettings(new Settings("fake", "fake"));
            return store;
        }
    }

    private sealed class MarkerStore : AsyncStoreEventStore
    {
        public MarkerStore(IStoreFactory f) : base(f.GetAsyncStore<EventRecord>()) { }
    }

    [Fact]
    public void Without_a_matching_override_the_default_store_is_answered()
    {
        new FakeFactory("Mongo").GetEventStore().Should().BeOfType<AsyncStoreEventStore>();
        new FakeFactory("Mongo").GetEventStore(o => o.Add("Other", f => f.Backend == "Raven", f => new MarkerStore(f)))
            .Should().BeOfType<AsyncStoreEventStore>();
    }

    [Fact]
    public void A_matching_override_is_answered()
        => new FakeFactory("Mongo").GetEventStore(o => o.Add("Mongo", f => f.Backend == "Mongo", f => new MarkerStore(f)))
            .Should().BeOfType<MarkerStore>();

    [Fact]
    public void Two_overrides_accepting_one_backend_are_refused_naming_both()
    {
        var act = () => new FakeFactory("Mongo").GetEventStore(o => o
            .Add("One", _ => true, f => new MarkerStore(f))
            .Add("Two", _ => true, f => new MarkerStore(f)));
        act.Should().Throw<InvalidOperationException>().WithMessage("*One, Two*Mongo*");
    }

    [Fact]
    public void An_override_name_registered_twice_is_refused()
    {
        var act = () => new FakeFactory("Mongo").GetEventStore(o => o
            .Add("Same", _ => false, f => new MarkerStore(f))
            .Add("same", _ => false, f => new MarkerStore(f)));
        act.Should().Throw<ArgumentException>().WithMessage("*'same' is already registered*");
    }

    [Theory]
    [InlineData("Json")]
    [InlineData("XML")]
    public void The_file_backends_are_refused_because_they_cannot_refuse_a_second_process(string backend)
    {
        var act = () => new FakeFactory(backend).GetEventStore();
        act.Should().Throw<NotSupportedException>().WithMessage($"*{backend}*across processes*");
    }
}
