using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Raven.Client.Documents.Session;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-665, live RavenDB: <see cref="AsyncRavenDBStore{T}.UpdateIfAsync"/> is a compare-and-set. It loads the document
/// by id, applies the update only where the condition holds on it, and saves with optimistic concurrency, so concurrent
/// increments all land. Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public class Task665UpdateIfLiveTests(ITestOutputHelper output)
{
    public class Counter : AbstractModel
    {
        public string? State { get; set; }
        public int Hits { get; set; }
    }

    private AsyncRavenDBStore<Counter>? Open()
    {
        var url = Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return null;
        }
        var store = new AsyncRavenDBStore<Counter>();
        store.SetSettings(new Settings(url, "birko_task665_" + Guid.NewGuid().ToString("N")));
        return store;
    }

    [Fact]
    public async Task The_update_applies_only_where_the_condition_holds_on_the_stored_document()
    {
        if (Open() is not { } store) return;
        var id = await store.CreateAsync(new Counter { State = "open" });

        (await store.UpdateIfAsync(id, c => c.State == "closed", c => c.Hits = 99)).Should().BeNull();
        (await store.ReadAsync(id))!.Hits.Should().Be(0, "a condition that does not hold writes nothing");

        var written = await store.UpdateIfAsync(id, c => c.State == "open", c => { c.State = "closed"; c.Hits = 1; });
        written!.State.Should().Be("closed");
        (await store.ReadAsync(id))!.Hits.Should().Be(1);

        (await store.UpdateIfAsync(Guid.NewGuid(), _ => true, c => c.Hits = 5)).Should().BeNull("a missing document is not created");
    }

    [Fact]
    public async Task Concurrent_conditional_increments_all_land()
    {
        if (Open() is not { } store) return;
        var id = await store.CreateAsync(new Counter { State = "open" });
        const int writers = 8;
        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(() => store.UpdateIfAsync(id, _ => true, c => c.Hits++))));

        (await store.ReadAsync(id))!.Hits.Should().Be(writers, "a conflict is loaded again, never written over");
    }

    [Fact]
    public async Task It_refuses_to_run_inside_a_unit_of_work()
    {
        if (Open() is not { } store) return;
        var id = await store.CreateAsync(new Counter { State = "open" });
        store.SetTransactionContext(System.Reflection.DispatchProxy.Create<IAsyncDocumentSession, FakeSession>());
        var act = () => store.UpdateIfAsync(id, _ => true, c => c.Hits = 1);
        await act.Should().ThrowAsync<InvalidOperationException>();
        store.SetTransactionContext(null);
    }

    /// <summary>Only its presence matters: the refusal happens before the session is touched.</summary>
    public class FakeSession : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }
}
