using Birko.Data.ElasticSearch.IndexManagement;
using FluentAssertions;
using Nest;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement
{
    /// <summary>
    /// TASK-560, live: a reindex started with <c>waitForCompletion: false</c> is pending and carries the task id a real
    /// cluster returns, and that id can be followed to the completed copy.
    /// <para>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
    /// </summary>
    public sealed class ReindexStartedLiveTests : IAsyncLifetime
    {
        private static readonly HttpClient Http = new();
        private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
        private readonly ITestOutputHelper _output;
        private readonly string _source = "birko560src" + Guid.NewGuid().ToString("N");
        private readonly string _target = "birko560dst" + Guid.NewGuid().ToString("N");

        public ReindexStartedLiveTests(ITestOutputHelper output) => _output = output;

        public async Task InitializeAsync()
        {
            if (string.IsNullOrWhiteSpace(Url)) return;
            foreach (var n in new[] { 1, 2, 3 })
            {
                var body = new StringContent($"{{\"n\":{n}}}", Encoding.UTF8, "application/json");
                (await Http.PostAsync($"{Url.TrimEnd('/')}/{_source}/_doc?refresh=true", body)).EnsureSuccessStatusCode();
            }
        }

        public async Task DisposeAsync()
        {
            if (string.IsNullOrWhiteSpace(Url)) return;
            try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{_source}"); } catch { }
            try { await Http.DeleteAsync($"{Url.TrimEnd('/')}/{_target}"); } catch { }
        }

        [Fact]
        public async Task A_started_reindex_is_pending_and_its_task_completes_the_copy()
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                _output.WriteLine("SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL.");
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                    throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_ES_URL is not.");
                return;
            }
            var client = new ElasticClient(new ConnectionSettings(new Uri(Url)));

            var result = await new ReindexHelper(client).ReindexAsync(_source, _target, waitForCompletion: false);

            result.Success.Should().BeFalse();
            result.IsPending.Should().BeTrue();
            result.TaskId.Should().MatchRegex("^[^:]+:[0-9]+$");

            GetTaskResponse? task = null;
            for (var i = 0; i < 100; i++)
            {
                task = await client.Tasks.GetTaskAsync(result.TaskId!);
                if (task.Completed) break;
                await Task.Delay(100);
            }
            task!.Completed.Should().BeTrue("the task id followed the reindex it names");
            await client.Indices.RefreshAsync(_target);
            (await client.CountAsync<object>(c => c.Index(_target))).Count.Should().Be(3);
        }
    }
}
