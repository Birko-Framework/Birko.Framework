using System;
using System.Text;
using Birko.Data.Migrations.ElasticSearch.Context;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// TASK-643: the schema builder read a failed existence check as "absent" and never checked the create or delete it led
/// to, the SH-M202 pattern TASK-152 fixed in the migration store. No server: an unreachable cluster, and a fake transport
/// that answers by HTTP method.
/// </summary>
public class Task643SchemaBuilderFailureTests
{
    /// <summary>Answers HEAD (exists) with <paramref name="head"/> and every other request with <paramref name="other"/>.</summary>
    private sealed class RoutedConnection : InMemoryConnection
    {
        private readonly int _head;
        private readonly int _other;
        public RoutedConnection(int head, int other) { _head = head; _other = other; }

        private (byte[] Body, int Status) For(RequestData request)
            => request.Method == Elasticsearch.Net.HttpMethod.HEAD
                ? (Array.Empty<byte>(), _head)
                : (Encoding.UTF8.GetBytes(_other >= 400 ? "{\"error\":{\"type\":\"cluster_block_exception\",\"reason\":\"read-only\"},\"status\":" + _other + "}" : "{\"acknowledged\":true}"), _other);

        public override TResponse Request<TResponse>(RequestData requestData)
        {
            var (body, status) = For(requestData);
            return ReturnConnectionStatus<TResponse>(requestData, body, status, "application/json");
        }

        public override System.Threading.Tasks.Task<TResponse> RequestAsync<TResponse>(RequestData requestData, System.Threading.CancellationToken cancellationToken)
        {
            var (body, status) = For(requestData);
            return ReturnConnectionStatusAsync<TResponse>(requestData, cancellationToken, body, status, "application/json");
        }
    }

    private static ElasticSearchSchemaBuilder Builder(int head, int other)
        => new(new ElasticClient(new ConnectionSettings(new SingleNodeConnectionPool(new Uri("http://localhost:9200")), new RoutedConnection(head, other))));

    private static ElasticSearchSchemaBuilder Unreachable()
        => new(new ElasticClient(new ConnectionSettings(new Uri("http://localhost:59993")).RequestTimeout(TimeSpan.FromSeconds(2))));

    [Fact]
    public void A_failed_existence_check_is_not_read_as_absent()
    {
        var schema = Unreachable();

        schema.Invoking(s => s.CreateCollection("orders")).Should().Throw<InvalidOperationException>().WithMessage("*existence check*");
        schema.Invoking(s => s.DropCollection("orders")).Should().Throw<InvalidOperationException>().WithMessage("*existence check*");
        schema.Invoking(s => s.CollectionExists("orders")).Should().Throw<InvalidOperationException>().WithMessage("*existence check*");
    }

    [Fact]
    public void A_refused_create_fails_the_step()
        => Builder(head: 404, other: 403).Invoking(s => s.CreateCollection("orders")).Should().Throw<InvalidOperationException>().WithMessage("*orders*");

    [Fact]
    public void A_refused_delete_fails_the_step()
        => Builder(head: 200, other: 403).Invoking(s => s.DropCollection("orders")).Should().Throw<InvalidOperationException>().WithMessage("*orders*");

    [Fact]
    public void Absent_and_present_answer_as_before()
    {
        Builder(head: 404, other: 200).CollectionExists("orders").Should().BeFalse();
        Builder(head: 200, other: 200).CollectionExists("orders").Should().BeTrue();
        Builder(head: 404, other: 200).Invoking(s => s.DropCollection("orders")).Should().NotThrow("an absent index is already dropped");
        Builder(head: 200, other: 200).Invoking(s => s.CreateCollection("orders")).Should().NotThrow("a present index is not created again");
        Builder(head: 404, other: 200).Invoking(s => s.CreateCollection("orders")).Should().NotThrow();
    }
}
