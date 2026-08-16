using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Birko.Data.CosmosDB.Stores;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-220 — the CosmosDB half of TASK-218. On .NET 9+ an <b>array</b>'s instance-style
/// <c>set.Contains(x.Col)</c> binds to <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)</c>, which the
/// Cosmos LINQ provider does not know: <c>NotSupportedException: Specified method is not supported</c>,
/// naming no method, while the <c>List&lt;T&gt;</c> spelling one keystroke away renders <c>IN (1, 5)</c>.
///
/// <para>
/// Non-gated, and that is the point: <c>ToQueryDefinition()</c> renders the SQL <b>locally</b> from a
/// <c>CosmosClient</c> built on a throwaway endpoint and the well-known emulator key — no account, no
/// emulator, no network. The sibling <c>CosmosFilterMatrixLiveTests</c> is gated on
/// <c>BIRKO_COSMOS_CONNECTION</c> and so had never run, which is exactly how this went unnoticed; a
/// translation defect does not need a live backend to be caught.
/// </para>
/// </summary>
public class CosmosSpanContainsTests
{
    public class Doc : AbstractModel { public int Amount { get; set; } }

    // Throwaway endpoint + the publicly documented emulator key. Never connected to. The timeouts are
    // squeezed because one test below DOES let the client attempt a connection (to prove translation
    // succeeded); at the SDK's defaults that retry storm costs ~25s.
    private static Container Offline() => new CosmosClient(
            "https://localhost:8081",
            "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                MaxRetryAttemptsOnRateLimitedRequests = 0,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.Zero,
                // Replace the network outright. One test below lets the client attempt a request, to
                // prove translation succeeded; pointing it at a dead port instead costs ~25s of SDK
                // retries. This fails instantly and deterministically, and cannot accidentally reach
                // anything real.
                HttpClientFactory = () => new System.Net.Http.HttpClient(new NoNetworkHandler()),
            })
        .GetContainer("probe", "probe");

    /// <summary>Fails every request immediately — see <see cref="Offline"/>.</summary>
    private sealed class NoNetworkHandler : System.Net.Http.HttpMessageHandler
    {
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => throw new System.Net.Http.HttpRequestException("offline test double");
    }

    private static string Render(Expression<Func<Doc, bool>> filter)
        => Offline().GetItemLinqQueryable<Doc>()
            .Where(Birko.Data.Expressions.SpanContains.Rewrite(filter))
            .ToQueryDefinition().QueryText;

    [Fact]
    public void An_array_backed_IN_renders_the_same_SQL_as_a_list_backed_one()
    {
        var arr = new[] { 1, 5 };
        var list = new List<int> { 1, 5 };

        Render(x => arr.Contains(x.Amount)).Should().Be(Render(x => list.Contains(x.Amount)));
        Render(x => arr.Contains(x.Amount)).Should().Contain("IN (1, 5)");
    }

    [Fact]
    public void Without_the_rewrite_the_provider_rejects_the_array_spelling()
    {
        // Pins the defect itself, so the test above cannot quietly become vacuous if the provider
        // starts supporting MemoryExtensions.Contains on its own — this is what would fail first.
        var arr = new[] { 1, 5 };
        Expression<Func<Doc, bool>> raw = x => arr.Contains(x.Amount);

        var act = () => Offline().GetItemLinqQueryable<Doc>().Where(raw).ToQueryDefinition();

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void The_negated_form_is_rewritten_too()
    {
        var arr = new[] { 1, 5 };

        Render(x => !arr.Contains(x.Amount)).Should().Contain("IN (1, 5)");
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    public void The_store_itself_gets_past_translation(string flavour)
    {
        // The three tests above call SpanContains.Rewrite directly, so they pin the HELPER. This one
        // pins the WIRING — that the stores actually apply it — and needs no server to do it: an
        // unwired store fails at TRANSLATION (NotSupportedException, before any I/O), a wired one gets
        // through translation and fails trying to reach the throwaway endpoint. Different exception,
        // different phase. Without this, unwiring every entry point breaks no test.
        var arr = new[] { 1, 5 };
        var container = Offline();

        Func<object?> act = flavour == "sync"
            ? () => new CosmosDBStore<Doc>(container).Read(x => arr.Contains(x.Amount)).ToList()
            : () => new AsyncCosmosDBStore<Doc>(container)
                        .ReadAsync(x => arr.Contains(x.Amount)).GetAwaiter().GetResult().ToList();

        act.Should().Throw<Exception>("the client's network is a handler that always fails")
            .Which.Should().NotBeOfType<NotSupportedException>(
                "a NotSupportedException means the filter never translated — the defect, not the network");
    }
}
