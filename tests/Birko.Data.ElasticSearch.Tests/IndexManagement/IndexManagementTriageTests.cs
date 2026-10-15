using Birko.Data.ElasticSearch.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
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
    /// TASK-154, the medium ElasticSearch index-management findings. A fake transport answers each request from a
    /// table, so a failure can be forced where a real cluster would need a fault, and what the manager asked for is
    /// recorded.
    /// </summary>
    public class IndexManagementTriageTests
    {
        private sealed class Fake : InMemoryConnection
        {
            private readonly Func<HttpMethod, string, (int Status, string Body)?> _answer;
            public List<string> Calls { get; } = new();

            public Fake(Func<HttpMethod, string, (int, string)?> answer) => _answer = answer;

            public override TResponse Request<TResponse>(RequestData r)
            {
                if (IsProductCheck(r)) return base.Request<TResponse>(r);
                var (status, body) = Answer(r);
                return ReturnConnectionStatus<TResponse>(r, Encoding.UTF8.GetBytes(body), status, "application/json");
            }

            public override Task<TResponse> RequestAsync<TResponse>(RequestData r, CancellationToken ct)
            {
                if (IsProductCheck(r)) return base.RequestAsync<TResponse>(r, ct);
                var (status, body) = Answer(r);
                return ReturnConnectionStatusAsync<TResponse>(r, ct, Encoding.UTF8.GetBytes(body), status, "application/json");
            }

            private static bool IsProductCheck(RequestData r) => r.Method == HttpMethod.GET && r.Uri.AbsolutePath.Trim('/').Length == 0;

            private (int, string) Answer(RequestData r)
            {
                var path = r.Uri.AbsolutePath.Trim('/');
                Calls.Add($"{r.Method} {path}");
                return _answer(r.Method, path) ?? (400, $@"{{""error"":""unexpected {r.Method} {path}""}}");
            }
        }

        private static (ElasticClient Client, Fake Fake) Client(Func<HttpMethod, string, (int, string)?> answer)
        {
            var fake = new Fake(answer);
            var settings = new ConnectionSettings(new SingleNodeConnectionPool(new Uri("http://localhost:9200")), fake).DisableDirectStreaming();
            return (new ElasticClient(settings), fake);
        }

        private const string ServerError = @"{""error"":{""type"":""cluster_block_exception"",""reason"":""blocked""},""status"":500}";

        /// <summary>SH-M233: a failed request answered "does not exist", so a delete reported done having done nothing.</summary>
        [Fact]
        public async Task A_failed_exists_request_throws_instead_of_answering_no()
        {
            var (client, _) = Client((m, p) => m == HttpMethod.HEAD ? (500, "") : null);
            var manager = new IndexManager(client);

            manager.Invoking(x => x.IndexExists("idx")).Should().Throw<InvalidOperationException>();
            await manager.Invoking(x => x.IndexExistsAsync("idx")).Should().ThrowAsync<InvalidOperationException>();
            await manager.Invoking(x => x.DeleteIndexAsync("idx")).Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task A_missing_index_still_answers_no()
        {
            var (client, _) = Client((m, p) => m == HttpMethod.HEAD ? (404, "") : null);
            (await new IndexManager(client).IndexExistsAsync("idx")).Should().BeFalse();
        }

        /// <summary>SH-M228: the mapping was sent without the index, so it landed wherever the client defaulted.</summary>
        [Fact]
        public void A_mapping_update_goes_to_the_named_index()
        {
            var (client, fake) = Client((m, p) => m == HttpMethod.PUT ? (200, @"{""acknowledged"":true}") : null);

            new IndexManager(client).UpdateMapping<MappedThing>("orders_v2", d => d.Properties(p => p.Keyword(k => k.Name(n => n.Code))));

            fake.Calls.Should().ContainSingle(c => c == "PUT orders_v2/_mapping");
        }

        public class MappedThing { public string Code { get; set; } = ""; }

        /// <summary>SH-M239: stats are keyed by the concrete index, so asking through an alias read all zeros.</summary>
        [Fact]
        public async Task Info_asked_through_an_alias_reports_the_index_it_names()
        {
            var (client, _) = Client((m, p) => (m, p) switch
            {
                (HttpMethod.GET, "orders/_stats") => (200, @"{""_shards"":{""total"":1,""successful"":1,""failed"":0},""indices"":{""orders_v3"":{""primaries"":{""docs"":{""count"":42},""store"":{""size_in_bytes"":1000}}}}}"),
                (HttpMethod.GET, "orders/_settings") => (200, @"{""orders_v3"":{""settings"":{""index"":{""number_of_shards"":""2"",""number_of_replicas"":""1""}}}}"),
                (HttpMethod.GET, "orders/_alias") => (200, @"{""orders_v3"":{""aliases"":{""orders"":{}}}}"),
                _ => null,
            });

            var info = await new IndexManager(client).GetIndexInfoAsync("orders");

            info.Name.Should().Be("orders_v3");
            info.DocumentCount.Should().Be(42);
            info.NumberOfShards.Should().Be(2);
        }

        /// <summary>SH-M236: a failed _cat/indices was an empty cluster.</summary>
        [Fact]
        public async Task A_failed_listing_throws_instead_of_answering_empty()
        {
            var (client, _) = Client((m, p) => p == "_cat/indices" ? (500, ServerError) : null);
            await new ElasticSearchIndexManagerAdapter(client).Invoking(a => a.ListAsync()).Should().ThrowAsync<IndexManagementException>();
        }

        /// <summary>SH-M237: every failure of GetInfoAsync read as "no such index". Only a missing index is null now.</summary>
        [Fact]
        public async Task Info_is_null_only_for_a_missing_index()
        {
            var (missing, _) = Client((m, p) => m == HttpMethod.HEAD ? (404, "") : null);
            (await new ElasticSearchIndexManagerAdapter(missing).GetInfoAsync("idx")).Should().BeNull();

            var (broken, _) = Client((m, p) => m == HttpMethod.HEAD ? (200, "") : (500, ServerError));
            await new ElasticSearchIndexManagerAdapter(broken).Invoking(a => a.GetInfoAsync("idx")).Should().ThrowAsync<InvalidOperationException>();
        }

        /// <summary>SH-M238: Unique / Sparse / ExpireAfter / unknown properties were dropped and the create reported success.</summary>
        [Theory]
        [InlineData("unique")]
        [InlineData("sparse")]
        [InlineData("ttl")]
        [InlineData("unknown-property")]
        [InlineData("non-numeric-shards")]
        public async Task A_flag_an_index_cannot_honour_is_refused(string flag)
        {
            var (client, fake) = Client((m, p) => (200, @"{""acknowledged"":true}"));
            var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition { Name = "idx", Fields = new List<Birko.Data.Patterns.IndexManagement.IndexField> { new() { Name = "code" } } };
            switch (flag)
            {
                case "unique": definition.Unique = true; break;
                case "sparse": definition.Sparse = true; break;
                case "ttl": definition.ExpireAfter = TimeSpan.FromDays(1); break;
                case "unknown-property": definition.Properties = new Dictionary<string, object> { ["Shards"] = 2 }; break;
                default: definition.Properties = new Dictionary<string, object> { ["NumberOfShards"] = "two" }; break;
            }

            await new ElasticSearchIndexManagerAdapter(client).Invoking(a => a.CreateAsync(definition)).Should().ThrowAsync<NotSupportedException>();
            fake.Calls.Should().NotContain(c => c.StartsWith("PUT"));
        }

        [Fact]
        public async Task A_shard_count_given_as_text_is_honoured()
        {
            var (client, fake) = Client((m, p) => m == HttpMethod.HEAD ? (404, "") : (200, @"{""acknowledged"":true,""index"":""idx""}"));
            var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition { Name = "idx", Properties = new Dictionary<string, object> { ["NumberOfShards"] = "3" } };

            await new ElasticSearchIndexManagerAdapter(client).CreateAsync(definition);
            fake.Calls.Should().Contain("PUT idx");
        }

        /// <summary>
        /// SH-M235: once the alias has moved the migration is done; a failed delete of the old index reported the whole
        /// reindex Failed, and a retry then died on "already exists".
        /// </summary>
        [Fact]
        public async Task A_failed_cleanup_after_the_alias_swap_is_a_success_with_a_cleanup_error()
        {
            const string complete = @"{""took"":1,""timed_out"":false,""total"":1,""created"":1,""updated"":0,""deleted"":0,""batches"":1,""version_conflicts"":0,""noops"":0,""failures"":[]}";
            var indices = new HashSet<string> { "old-index" };
            (int, string)? Answer(HttpMethod m, string p) => (m, p) switch
            {
                (HttpMethod.HEAD, _) => (indices.Contains(p) ? 200 : 404, ""),
                (HttpMethod.PUT, _) => Created(p),
                (HttpMethod.DELETE, "old-index") => (500, ServerError),
                (HttpMethod.GET, "_alias") => (200, @"{""old-index"":{""aliases"":{""products"":{}}}}"),
                (HttpMethod.POST, "_reindex") => (200, complete),
                (HttpMethod.POST, "_aliases") => (200, @"{""acknowledged"":true}"),
                (HttpMethod.POST, _) when p.EndsWith("_refresh") => (200, @"{""_shards"":{""total"":1,""successful"":1,""failed"":0}}"),
                _ => null,
            };
            (int, string) Created(string p) { indices.Add(p); return (200, $@"{{""acknowledged"":true,""index"":""{p}""}}"); }

            var (client, _) = Client(Answer);
            var helper = new ReindexHelper(client);

            var sync = helper.ReindexWithAlias("products", "new-index", deleteOldIndex: true);
            sync.Success.Should().BeTrue();
            sync.CleanupError.Should().NotBeNullOrEmpty();

            indices.Remove("new-index");
            var async = await helper.ReindexWithAliasAsync("products", "new-index", deleteOldIndex: true);
            async.Success.Should().BeTrue();
            async.CleanupError.Should().NotBeNullOrEmpty();
        }
    }
}
