using Birko.Data.ElasticSearch.UnitOfWork;
using Birko.Data.Patterns.UnitOfWork;
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

namespace Birko.Data.ElasticSearch.Tests.UnitOfWork
{
    /// <summary>
    /// SH-M426 (TASK-325) — a commit whose bulk request partly failed left the unit of work active with <b>every</b>
    /// operation still buffered, including the ones Elasticsearch had already applied, and the exception carried no
    /// per-item outcome, so a caller could neither tell what failed nor retry only that. A failed commit now ends the
    /// unit of work (the buffer was sent; it is not pending any more) and throws
    /// <see cref="ElasticSearchBulkCommitException"/> listing each failed item. A rollback that answers the failed
    /// commit is a no-op, so catch-and-roll-back keeps the commit's exception — the same rule as <c>SqlUnitOfWork</c>.
    ///
    /// <para>Measured while fixing: NEST 7's <c>BulkResponse.IsValid</c> is already false when any item failed, so the
    /// old <c>response.Errors</c> branch was unreachable and a partial failure surfaced as "failed: Unknown error".</para>
    /// </summary>
    public class ElasticSearchUnitOfWorkCommitFailureTests
    {
        private const string PartialFailure = @"{""took"":3,""errors"":true,""items"":[
            {""index"":{""_index"":""docs"",""_type"":""_doc"",""_id"":""1"",""status"":201,""result"":""created""}},
            {""index"":{""_index"":""docs"",""_type"":""_doc"",""_id"":""2"",""status"":400,""error"":{""type"":""mapper_parsing_exception"",""reason"":""failed to parse field [n]""}}},
            {""update"":{""_index"":""docs"",""_type"":""_doc"",""_id"":""3"",""status"":404,""error"":{""type"":""document_missing_exception"",""reason"":""[_doc][3]: document missing""}}}]}";

        // Real ES 7 items carry `_type`; NEST 7's item IsValid is false without it, so a fixture omitting it would
        // make every item look failed.
        private const string AllSucceeded = @"{""took"":3,""errors"":false,""items"":[
            {""index"":{""_index"":""docs"",""_type"":""_doc"",""_id"":""1"",""status"":201,""result"":""created""}}]}";

        private sealed class Doc
        {
            public string Id { get; set; } = "";
            public int N { get; set; }
        }

        private sealed class FakeCluster : InMemoryConnection
        {
            private readonly Queue<string> _bulkBodies;
            public int BulkRequests { get; private set; }

            public FakeCluster(params string[] bulkBodies) => _bulkBodies = new Queue<string>(bulkBodies);

            public override TResponse Request<TResponse>(RequestData requestData)
            {
                if (IsProductCheck(requestData)) return base.Request<TResponse>(requestData);
                return ReturnConnectionStatus<TResponse>(requestData, Encoding.UTF8.GetBytes(Next()), 200, "application/json");
            }

            public override Task<TResponse> RequestAsync<TResponse>(RequestData requestData, CancellationToken cancellationToken)
            {
                if (IsProductCheck(requestData)) return base.RequestAsync<TResponse>(requestData, cancellationToken);
                return ReturnConnectionStatusAsync<TResponse>(requestData, cancellationToken, Encoding.UTF8.GetBytes(Next()), 200, "application/json");
            }

            private string Next()
            {
                BulkRequests++;
                return _bulkBodies.Count > 1 ? _bulkBodies.Dequeue() : _bulkBodies.Peek();
            }

            private static bool IsProductCheck(RequestData request)
                => request.Method == HttpMethod.GET && request.Uri.AbsolutePath.Trim('/').Length == 0;
        }

        private static (ElasticSearchUnitOfWork uow, FakeCluster cluster) Create(params string[] bulkBodies)
        {
            var cluster = new FakeCluster(bulkBodies);
            var settings = new ConnectionSettings(new SingleNodeConnectionPool(new Uri("http://localhost:9200")), cluster)
                .DisableDirectStreaming();
            return (new ElasticSearchUnitOfWork(new ElasticClient(settings)), cluster);
        }

        private static void Buffer(ElasticSearchUnitOfWork uow)
        {
            uow.Context!.Index(new Doc { Id = "1", N = 1 }, "docs");
            uow.Context.Index(new Doc { Id = "2", N = 2 }, "docs");
            uow.Context.Update("3", new Doc { Id = "3", N = 3 }, "docs");
        }

        [Fact]
        public async Task A_partially_failed_commit_names_each_failed_item()
        {
            var (uow, _) = Create(PartialFailure);
            await uow.BeginAsync();
            Buffer(uow);

            var thrown = await FluentActions.Awaiting(() => uow.CommitAsync()).Should().ThrowAsync<ElasticSearchBulkCommitException>();

            var ex = thrown.Which;
            ex.SucceededCount.Should().Be(1);
            ex.Failures.Should().HaveCount(2);
            ex.Failures.Should().ContainSingle(f => f.Id == "2" && f.Operation == "index" && f.Status == 400
                && f.Reason!.Contains("failed to parse"));
            ex.Failures.Should().ContainSingle(f => f.Id == "3" && f.Operation == "update" && f.Status == 404
                && f.Reason!.Contains("document missing"));
            ex.Message.Should().Contain("2 of 3");
        }

        [Fact]
        public async Task A_failed_commit_ends_the_unit_of_work_and_drops_the_sent_buffer()
        {
            var (uow, cluster) = Create(PartialFailure, AllSucceeded);
            await uow.BeginAsync();
            Buffer(uow);
            await FluentActions.Awaiting(() => uow.CommitAsync()).Should().ThrowAsync<UnitOfWorkException>();

            uow.IsActive.Should().BeFalse("the buffer was sent — keeping it queued invited a retry that re-sends applied items");
            uow.Context.Should().BeNull();

            // A fresh boundary starts empty: the applied item is not re-sent.
            await uow.BeginAsync();
            uow.Context!.Operations.Should().BeEmpty();
            cluster.BulkRequests.Should().Be(1);
        }

        [Fact]
        public async Task Catch_and_roll_back_after_a_failed_commit_keeps_the_commits_exception()
        {
            var (uow, _) = Create(PartialFailure);
            await uow.BeginAsync();
            Buffer(uow);

            async Task Work()
            {
                try
                {
                    await uow.CommitAsync();
                }
                catch
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }

            await FluentActions.Awaiting(Work).Should().ThrowAsync<ElasticSearchBulkCommitException>();
        }

        [Fact]
        public async Task A_successful_commit_ends_the_unit_of_work()
        {
            // Contract pin: the success path.
            var (uow, cluster) = Create(AllSucceeded);
            await uow.BeginAsync();
            uow.Context!.Index(new Doc { Id = "1", N = 1 }, "docs");

            await uow.CommitAsync();

            uow.IsActive.Should().BeFalse();
            cluster.BulkRequests.Should().Be(1);
        }

        [Fact]
        public async Task Rollback_without_a_unit_of_work_still_throws()
        {
            // Contract pin: only the rollback that answers a failed commit is excused.
            var (uow, _) = Create(AllSucceeded);

            await FluentActions.Awaiting(() => uow.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
        }
    }
}
