using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-1004 and TASK-1005, on <c>RavenDatabaseBootstrap</c> (TASK-996).
/// <list type="bullet">
/// <item>TASK-1004: the create retry caught every exception, so a create that can never succeed was sent again for 30 s
/// before it surfaced. Only the two refusals a racing create gets are retried now. A database name the server will not
/// accept is listed as missing and then refused on create: it used to take the full 30 s.</item>
/// <item>TASK-1005: the async store's init ran the sync walk, which slept on the calling thread for up to 30 s. A disabled
/// database answers every request with <c>DatabaseDisabledException</c>, so the walk waits; the first call now returns a
/// pending task at once.</item>
/// </list>
/// Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public class Task1004DatabaseBootstrapLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task1004DatabaseBootstrapLiveTests(ITestOutputHelper output) => _output = output;

    public class Item : AbstractModel
    {
        public string? Name { get; set; }
    }

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    /// <summary>Listed as missing, refused on create ("not permitted"), for a reason that is not the race.</summary>
    private const string RefusedName = "birko task1004 not permitted";

    [Fact]
    public async Task An_async_store_on_a_database_the_server_refuses_to_create_fails_at_once()
    {
        if (!RequireServer()) return;
        using var store = new AsyncRavenDBStore<Item>(Url!, RefusedName);
        var clock = Stopwatch.StartNew();

        await FluentActions.Awaiting(() => store.CountAsync()).Should().ThrowAsync<Exception>();

        _output.WriteLine($"failed after {clock.ElapsedMilliseconds} ms");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "only a racing create's refusals are retried");
    }

    [Fact]
    public void A_sync_store_on_a_database_the_server_refuses_to_create_fails_at_once()
    {
        if (!RequireServer()) return;
        using var store = new RavenDBStore<Item>(Url!, RefusedName);
        var clock = Stopwatch.StartNew();

        FluentActions.Invoking(() => store.Count()).Should().Throw<Exception>();

        _output.WriteLine($"failed after {clock.ElapsedMilliseconds} ms");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "only a racing create's refusals are retried");
    }

    /// <summary>The caller's token does not stop it, by design: one-time init is shared, so it runs under
    /// <see cref="CancellationToken.None"/> (TASK-324). What TASK-1005 changes is that the wait holds no thread.</summary>
    [Fact]
    public async Task An_async_store_waiting_for_a_disabled_database_does_not_hold_the_callers_thread()
    {
        if (!RequireServer()) return;
        var db = "birko-task1005-" + Guid.NewGuid().ToString("N");
        using var admin = new DocumentStore { Urls = new[] { Url! } }.Initialize();
        admin.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(db)));
        try
        {
            admin.Maintenance.Server.Send(new ToggleDatabasesStateOperation(db, disable: true));
            using var store = new AsyncRavenDBStore<Item>(Url!, db);
            var clock = Stopwatch.StartNew();

            var count = store.CountAsync();
            var returnedAfter = clock.Elapsed;
            _output.WriteLine($"CountAsync returned its task after {returnedAfter.TotalMilliseconds:F0} ms (completed: {count.IsCompleted})");

            returnedAfter.Should().BeLessThan(TimeSpan.FromSeconds(2), "the wait is awaited, not slept on the caller's thread");
            count.IsCompleted.Should().BeFalse("the database answers nothing while it is disabled");
            await FluentActions.Awaiting(() => count).Should().ThrowAsync<Raven.Client.Exceptions.Database.DatabaseDisabledException>();
            _output.WriteLine($"failed after {clock.ElapsedMilliseconds} ms");
        }
        finally
        {
            try { admin.Maintenance.Server.Send(new DeleteDatabasesOperation(db, hardDelete: true)); } catch { }
        }
    }
}
