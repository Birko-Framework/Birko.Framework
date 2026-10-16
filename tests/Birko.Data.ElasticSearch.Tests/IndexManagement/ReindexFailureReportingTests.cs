using Birko.Data.ElasticSearch.IndexManagement;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using HttpMethod = Elasticsearch.Net.HttpMethod;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement
{
    /// <summary>
    /// SH-H038 (TASK-319) — a reindex that stopped early answers HTTP <b>200</b> with no failures listed, having copied
    /// only part of the source: <c>timed_out: true</c>, or a task cancelled mid-copy. <c>ReindexHelper</c> reported it <c>Successful</c>, so
    /// <c>ReindexWithAlias</c> swapped the alias to the partial index and, with <c>deleteOldIndex: true</c>, deleted the
    /// only complete copy. The finding also claimed the same for per-document <c>failures</c>; that half is refuted —
    /// NEST's <c>IsValid</c> is already false for them — and the tests for it are <b>contract pins</b>: they pass with
    /// or without the fix, and fail if NEST ever stops folding failures into <c>IsValid</c>.
    ///
    /// <para>No cluster is needed: a fake transport answers each request by path and records what was asked of it,
    /// so the assertions are on what the helper <b>did</b> to the indices — not only on the result it returned.</para>
    /// </summary>
    public class ReindexFailureReportingTests
    {
        private const string PartialFailure = @"{""took"":12,""timed_out"":false,""total"":3,""created"":2,""updated"":0,""deleted"":0,""batches"":1,
            ""version_conflicts"":1,""noops"":0,""failures"":[{""index"":""new-index"",""id"":""doc-3"",""status"":409,
            ""cause"":{""type"":""version_conflict_engine_exception"",""reason"":""[doc-3]: version conflict, document already exists""}}]}";

        private const string TimedOut = @"{""took"":60000,""timed_out"":true,""total"":3,""created"":1,""updated"":0,""deleted"":0,""batches"":1,
            ""version_conflicts"":0,""noops"":0,""failures"":[]}";

        // A task cancelled mid-copy (POST _tasks/<id>/_cancel) also answers 200: not timed out, no failures, counts short.
        private const string Cancelled = @"{""took"":900,""timed_out"":false,""total"":3,""created"":1,""updated"":0,""deleted"":0,""batches"":1,
            ""version_conflicts"":0,""noops"":0,""canceled"":""by user request"",""failures"":[]}";

        // waitForCompletion: false — ElasticSearch answers at once with a task id and no counts (TASK-560).
        private const string Started = @"{""task"":""oTUltX4IQMOUUVeiohTt8A:12345""}";

        private const string Complete = @"{""took"":12,""timed_out"":false,""total"":3,""created"":3,""updated"":0,""deleted"":0,""batches"":1,
            ""version_conflicts"":0,""noops"":0,""failures"":[]}";

        /// <summary>A transport that keeps a set of existing indices and answers the reindex with a canned body.</summary>
        private sealed class FakeCluster : InMemoryConnection
        {
            private readonly string _reindexBody;
            public HashSet<string> Indices { get; } = new(StringComparer.OrdinalIgnoreCase) { "old-index" };
            public List<string> Calls { get; } = new();

            public FakeCluster(string reindexBody) => _reindexBody = reindexBody;

            public override TResponse Request<TResponse>(RequestData requestData)
            {
                // The client's startup product check (GET /) is answered by InMemoryConnection itself.
                if (IsProductCheck(requestData)) return base.Request<TResponse>(requestData);
                var (status, body) = Answer(requestData);
                return ReturnConnectionStatus<TResponse>(requestData, Encoding.UTF8.GetBytes(body), status, "application/json");
            }

            public override Task<TResponse> RequestAsync<TResponse>(RequestData requestData, CancellationToken cancellationToken)
            {
                if (IsProductCheck(requestData)) return base.RequestAsync<TResponse>(requestData, cancellationToken);
                var (status, body) = Answer(requestData);
                return ReturnConnectionStatusAsync<TResponse>(requestData, cancellationToken, Encoding.UTF8.GetBytes(body), status, "application/json");
            }

            private static bool IsProductCheck(RequestData request)
                => request.Method == HttpMethod.GET && request.Uri.AbsolutePath.Trim('/').Length == 0;

            private (int, string) Answer(RequestData request)
            {
                var path = request.Uri.AbsolutePath.Trim('/');
                Calls.Add($"{request.Method} {path}");
                switch (request.Method)
                {
                    case HttpMethod.HEAD:
                        return (Indices.Contains(path) ? 200 : 404, "");
                    case HttpMethod.PUT:
                        Indices.Add(path);
                        return (200, $@"{{""acknowledged"":true,""shards_acknowledged"":true,""index"":""{path}""}}");
                    case HttpMethod.DELETE:
                        Indices.Remove(path);
                        return (200, @"{""acknowledged"":true}");
                    case HttpMethod.GET when path == "_alias":
                        return (200, @"{""old-index"":{""aliases"":{""products"":{}}}}");
                    case HttpMethod.POST when path == "_reindex":
                        return (200, _reindexBody);
                    case HttpMethod.POST when path == "_aliases":
                        return (200, @"{""acknowledged"":true}");
                    case HttpMethod.POST when path.EndsWith("_refresh"):
                        return (200, @"{""_shards"":{""total"":1,""successful"":1,""failed"":0}}");
                    default:
                        return (400, $@"{{""error"":""unexpected {request.Method} {path}""}}");
                }
            }
        }

        private static (ReindexHelper helper, FakeCluster cluster) Create(string reindexBody)
        {
            var cluster = new FakeCluster(reindexBody);
            var settings = new ConnectionSettings(new SingleNodeConnectionPool(new Uri("http://localhost:9200")), cluster)
                .DisableDirectStreaming();
            return (new ReindexHelper(new ElasticClient(settings)), cluster);
        }

        [Fact]
        public void Reindex_with_per_document_failures_is_a_failure()
        {
            var (helper, _) = Create(PartialFailure);

            var result = helper.Reindex("old-index", "new-index");

            result.Success.Should().BeFalse();
            result.Failures.Should().Be(1);
            result.DocumentsProcessed.Should().Be(2);
            result.ErrorMessage.Should().Contain("version conflict");
        }

        [Fact]
        public async Task ReindexAsync_with_per_document_failures_is_a_failure()
        {
            var (helper, _) = Create(PartialFailure);

            var result = await helper.ReindexAsync("old-index", "new-index");

            result.Success.Should().BeFalse();
            result.Failures.Should().Be(1);
        }

        [Fact]
        public void ReindexWithScript_with_per_document_failures_is_a_failure()
        {
            var (helper, _) = Create(PartialFailure);

            helper.ReindexWithScript("old-index", "new-index", "ctx._source.x = 1").Success.Should().BeFalse();
        }

        [Fact]
        public async Task ReindexWithScriptAsync_with_per_document_failures_is_a_failure()
        {
            var (helper, _) = Create(PartialFailure);

            (await helper.ReindexWithScriptAsync("old-index", "new-index", "ctx._source.x = 1")).Success.Should().BeFalse();
        }

        [Fact]
        public void Reindex_that_timed_out_is_a_failure()
        {
            var (helper, _) = Create(TimedOut);

            var result = helper.Reindex("old-index", "new-index");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("timed out").And.Contain("1 of 3");
        }

        [Fact]
        public void ReindexWithAlias_with_failures_keeps_the_source_and_the_alias()
        {
            var (helper, cluster) = Create(PartialFailure);

            var result = helper.ReindexWithAlias("products", "new-index", deleteOldIndex: true);

            result.Success.Should().BeFalse();
            cluster.Indices.Should().Contain("old-index", "the only complete copy must survive")
                .And.NotContain("new-index", "the incomplete copy is cleaned up");
            cluster.Calls.Should().NotContain("POST _aliases", "the alias must not move to an incomplete index");
        }

        [Fact]
        public async Task ReindexWithAliasAsync_with_failures_keeps_the_source_and_the_alias()
        {
            var (helper, cluster) = Create(PartialFailure);

            var result = await helper.ReindexWithAliasAsync("products", "new-index", deleteOldIndex: true);

            result.Success.Should().BeFalse();
            cluster.Indices.Should().Contain("old-index").And.NotContain("new-index");
            cluster.Calls.Should().NotContain("POST _aliases");
        }

        [Fact]
        public void ReindexWithAlias_that_timed_out_keeps_the_source_and_the_alias()
        {
            var (helper, cluster) = Create(TimedOut);

            var result = helper.ReindexWithAlias("products", "new-index", deleteOldIndex: true);

            result.Success.Should().BeFalse();
            cluster.Indices.Should().Contain("old-index", "the only complete copy must survive")
                .And.NotContain("new-index", "the partial copy is cleaned up");
            cluster.Calls.Should().NotContain("POST _aliases", "the alias must not move to a partial index");
        }

        [Fact]
        public async Task ReindexWithAliasAsync_that_timed_out_keeps_the_source_and_the_alias()
        {
            var (helper, cluster) = Create(TimedOut);

            var result = await helper.ReindexWithAliasAsync("products", "new-index", deleteOldIndex: true);

            result.Success.Should().BeFalse();
            cluster.Indices.Should().Contain("old-index").And.NotContain("new-index");
            cluster.Calls.Should().NotContain("POST _aliases");
        }

        [Fact]
        public async Task The_other_three_variants_report_a_timeout_as_a_failure()
        {
            (await Create(TimedOut).helper.ReindexAsync("old-index", "new-index")).Success.Should().BeFalse();
            Create(TimedOut).helper.ReindexWithScript("old-index", "new-index", "ctx._source.x = 1").Success.Should().BeFalse();
            (await Create(TimedOut).helper.ReindexWithScriptAsync("old-index", "new-index", "ctx._source.x = 1")).Success.Should().BeFalse();
        }

        [Fact]
        public void Reindex_flagged_timed_out_is_a_failure_even_when_the_counts_look_complete()
        {
            // The server's own timeout flag is trusted over counts that add up: a bulk request it timed out may have
            // been counted before it was known to have failed. Without this case nothing exercises the flag, because
            // every other timed-out response here is also short.
            var (helper, _) = Create(@"{""took"":60000,""timed_out"":true,""total"":3,""created"":3,""updated"":0,""batches"":1,
                ""version_conflicts"":0,""noops"":0,""failures"":[]}");

            var result = helper.Reindex("old-index", "new-index");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("timed out");
        }

        [Fact]
        public void Reindex_that_was_cancelled_is_a_failure()
        {
            var (helper, _) = Create(Cancelled);

            var result = helper.Reindex("old-index", "new-index");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("stopped").And.Contain("1 of 3");
        }

        [Fact]
        public async Task ReindexWithAliasAsync_that_was_cancelled_keeps_the_source_and_the_alias()
        {
            var (helper, cluster) = Create(Cancelled);

            var result = await helper.ReindexWithAliasAsync("products", "new-index", deleteOldIndex: true);

            result.Success.Should().BeFalse();
            cluster.Indices.Should().Contain("old-index").And.NotContain("new-index");
            cluster.Calls.Should().NotContain("POST _aliases");
        }

        [Fact]
        public void ReindexWithAlias_that_copied_everything_swaps_and_deletes()
        {
            // The success path still works end to end — the guard is not "never swap".
            var (helper, cluster) = Create(Complete);

            var result = helper.ReindexWithAlias("products", "new-index", deleteOldIndex: true);

            result.Success.Should().BeTrue();
            result.DocumentsProcessed.Should().Be(3);
            cluster.Calls.Should().Contain("POST _aliases");
            cluster.Indices.Should().Contain("new-index").And.NotContain("old-index");
        }

        /// <summary>
        /// TASK-560: a reindex that was only started reported <c>Successful</c> with 0 documents, after refreshing a
        /// target still being written. It is pending, not a success, and carries the task id to follow.
        /// </summary>
        [Fact]
        public void Reindex_without_waiting_is_pending_with_its_task_id()
        {
            var (helper, cluster) = Create(Started);

            var result = helper.Reindex("old-index", "new-index", waitForCompletion: false);

            result.Success.Should().BeFalse("nothing has been copied yet");
            result.IsPending.Should().BeTrue();
            result.TaskId.Should().Be("oTUltX4IQMOUUVeiohTt8A:12345");
            result.ErrorMessage.Should().Contain("oTUltX4IQMOUUVeiohTt8A:12345");
            cluster.Calls.Should().NotContain(c => c.EndsWith("_refresh"), "the target is still being written");
        }

        [Fact]
        public async Task ReindexAsync_without_waiting_is_pending_with_its_task_id()
        {
            var (helper, cluster) = Create(Started);

            var result = await helper.ReindexAsync("old-index", "new-index", waitForCompletion: false);

            result.Success.Should().BeFalse();
            result.IsPending.Should().BeTrue();
            result.TaskId.Should().Be("oTUltX4IQMOUUVeiohTt8A:12345");
            cluster.Calls.Should().NotContain(c => c.EndsWith("_refresh"));
        }

        [Fact]
        public void A_started_reindex_with_no_task_id_is_a_failure_not_pending()
        {
            var (helper, _) = Create(@"{}");

            var result = helper.Reindex("old-index", "new-index", waitForCompletion: false);

            result.Success.Should().BeFalse();
            result.IsPending.Should().BeFalse("there is nothing to follow");
            result.ErrorMessage.Should().Contain("task id");
        }

        [Fact]
        public void A_completed_reindex_is_not_pending()
        {
            var (helper, _) = Create(Complete);

            var result = helper.Reindex("old-index", "new-index");

            result.Success.Should().BeTrue();
            result.IsPending.Should().BeFalse();
            result.TaskId.Should().BeNull();
        }
    }
}
