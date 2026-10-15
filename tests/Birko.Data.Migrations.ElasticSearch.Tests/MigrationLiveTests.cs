using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.ElasticSearch.Context;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// TASK-152, live ElasticSearch: a count on a missing index fails (SH-M197), a rename reports and survives awkward names
/// (SH-M199), null and not-equal filters mean what the filter dialect means (SH-M176), and the migrations index maps
/// Version as a number (SH-M201). Gated on <c>BIRKO_ES_URL</c>.
/// </summary>
public sealed class MigrationLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly ElasticClient? _client;
    private readonly string _prefix = "birko-task152-" + Guid.NewGuid().ToString("N");

    public MigrationLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live ElasticSearch. Set BIRKO_ES_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _client = new ElasticClient(new ConnectionSettings(new Uri(Url!)).DisableDirectStreaming());
    }

    public void Dispose()
    {
        if (_client == null) return;
        try { _client.Indices.Delete(_prefix + "*"); } catch { }
    }

    private string Seed()
    {
        var index = _prefix + "-rows";
        var data = new ElasticSearchDataMigrator(_client!);
        data.BulkInsert(index, new[]
        {
            new Dictionary<string, object> { ["kind"] = "a", ["first name"] = "Ann" },
            new Dictionary<string, object> { ["kind"] = "b", ["first name"] = "Bob" },
            new Dictionary<string, object> { ["other"] = 1 },
        });
        return index;
    }

    [Fact]
    public void A_count_on_a_missing_index_fails_instead_of_answering_zero()
    {
        if (_client == null) return;
        new ElasticSearchDataMigrator(_client).Invoking(d => d.CountDocuments(_prefix + "-missing", "{\"kind\":\"a\"}"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Null_and_not_equal_mean_what_the_filter_means()
    {
        if (_client == null) return;
        var index = Seed();
        var data = new ElasticSearchDataMigrator(_client);

        data.CountDocuments(index, "{\"kind.keyword\":null}").Should().Be(1, "the document without kind");
        data.CountDocuments(index, "{\"kind.keyword\":{\"$ne\":null}}").Should().Be(2);
        data.CountDocuments(index, "{\"kind.keyword\":{\"$ne\":\"a\"}}").Should().Be(2, "b, and the document without kind");
        data.Invoking(d => d.CountDocuments(index, "{\"kind\":{\"$in\":[\"a\"]}}")).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_rename_touches_the_documents_that_have_the_field_and_reports_a_refusal()
    {
        if (_client == null) return;
        var index = Seed();
        var schema = new ElasticSearchSchemaBuilder(_client);

        schema.RenameField(index, "first name", "given 'name'");

        var docs = _client.Search<Dictionary<string, object>>(s => s.Index(index).Size(10)).Documents.ToList();
        docs.Count(d => d.ContainsKey("given 'name'")).Should().Be(2);
        docs.Should().NotContain(d => d.ContainsKey("first name"));
        docs.Single(d => d.ContainsKey("other")).Should().NotContainKey("given 'name'", "a document without the field gains nothing");

        schema.Invoking(s => s.RenameField(_prefix + "-missing", "a", "b")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_migrations_index_maps_Version_as_a_number()
    {
        if (_client == null) return;
        var store = new ElasticSearchMigrationStore(_client, new Settings.ElasticSearchMigrationSettings { MigrationsIndex = _prefix + "-migrations" });
        store.Initialize();

        var mapping = _client.LowLevel.Indices.GetMapping<StringResponse>(_prefix + "-migrations").Body;
        _output.WriteLine(mapping);
        mapping.Should().Contain("\"type\":\"long\"");
        store.GetAppliedVersions().Should().BeEmpty();
    }
}
