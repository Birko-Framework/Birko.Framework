using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.IndexManagement;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement;

/// <summary>
/// TASK-648, live: a cancellation that arrives during an alias reindex. After the swap the reindex is done, so a cancelled
/// old-index delete is a cleanup error on a successful result; before the swap the cancellation is the caller's and
/// propagates, and the half-built new index is removed so a retry can create it.
/// </summary>
public sealed class Task648ReindexCancellationLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private static readonly HttpClient Http = new();
    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko648" + Guid.NewGuid().ToString("N").Substring(0, 12);

    public Task648ReindexCancellationLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (!string.IsNullOrWhiteSpace(Url)) return;
        const string message = "SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url!.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }

    /// <summary>Cancels the caller's token when it sees the first request matching <c>trigger</c>, then sends it.</summary>
    private sealed class CancellingConnection : HttpConnection
    {
        private readonly Func<RequestData, bool> _trigger;
        private readonly CancellationTokenSource _cts;
        public CancellingConnection(Func<RequestData, bool> trigger, CancellationTokenSource cts) { _trigger = trigger; _cts = cts; }

        public override Task<TResponse> RequestAsync<TResponse>(RequestData requestData, CancellationToken cancellationToken)
        {
            if (_trigger(requestData)) _cts.Cancel();
            return base.RequestAsync<TResponse>(requestData, cancellationToken);
        }
    }

    private (ReindexHelper Helper, ElasticClient Plain) Helper(Func<RequestData, bool> trigger, CancellationTokenSource cts)
    {
        var settings = new ConnectionSettings(new SingleNodeConnectionPool(new Uri(Url!)), new CancellingConnection(trigger, cts));
        return (new ReindexHelper(new ElasticClient(settings)), new ElasticClient(new ConnectionSettings(new Uri(Url!))));
    }

    private async Task<(string Alias, string Old, string New)> SeedAsync(ElasticClient plain)
    {
        var alias = _prefix + "-alias";
        var old = _prefix + "-v1";
        (await plain.Indices.CreateAsync(old, c => c.Aliases(a => a.Alias(alias)))).IsValid.Should().BeTrue();
        for (var i = 0; i < 3; i++)
            await plain.IndexAsync(new { n = i }, d => d.Index(old).Refresh(Refresh.True));
        return (alias, old, _prefix + "-v2");
    }

    [Fact]
    public async Task A_cancelled_old_index_delete_after_the_swap_is_a_cleanup_error_on_a_successful_result()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        using var cts = new CancellationTokenSource();
        string? old = null;
        var (helper, plain) = Helper(r => r.Method == Elasticsearch.Net.HttpMethod.DELETE && old != null && r.PathAndQuery.StartsWith(old), cts);
        var (alias, oldIndex, newIndex) = await SeedAsync(plain);
        old = oldIndex;

        var result = await helper.ReindexWithAliasAsync(alias, newIndex, deleteOldIndex: true, ct: cts.Token);
        _output.WriteLine($"success={result.Success} error={result.ErrorMessage} cleanup={result.CleanupError}");

        result.Success.Should().BeTrue("the alias already points at the new index");
        result.CleanupError.Should().NotBeNullOrEmpty();
        (await plain.Indices.GetAliasAsync(Indices.All, a => a.Name(alias))).Indices.Keys.Should().ContainSingle(k => k.Name == newIndex);
    }

    [Fact]
    public async Task A_cancellation_before_the_swap_propagates_and_removes_the_new_index()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        using var cts = new CancellationTokenSource();
        var (helper, plain) = Helper(r => r.PathAndQuery.StartsWith("_reindex") || r.PathAndQuery.StartsWith("/_reindex"), cts);
        var (alias, oldIndex, newIndex) = await SeedAsync(plain);

        var act = () => helper.ReindexWithAliasAsync(alias, newIndex, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await plain.Indices.ExistsAsync(newIndex)).Exists.Should().BeFalse("a retry must be able to create it");
        (await plain.Indices.GetAliasAsync(Indices.All, a => a.Name(alias))).Indices.Keys.Should().ContainSingle(k => k.Name == oldIndex);
    }

    [Fact]
    public async Task A_cancelled_plain_reindex_propagates()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        using var cts = new CancellationTokenSource();
        var (helper, plain) = Helper(r => r.PathAndQuery.StartsWith("_reindex") || r.PathAndQuery.StartsWith("/_reindex"), cts);
        var (_, oldIndex, newIndex) = await SeedAsync(plain);
        await plain.Indices.CreateAsync(newIndex);

        var act = () => helper.ReindexAsync(oldIndex, newIndex, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
