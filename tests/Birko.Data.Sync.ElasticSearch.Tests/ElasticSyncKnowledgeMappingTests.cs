using System;
using System.Linq;
using System.Reflection;
using Birko.Data.Sync.ElasticSearch.Models;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Sync.ElasticSearch.Tests;

/// <summary>
/// CR-H101: ElasticSyncKnowledgeItem.Id was annotated [Text(Name = "_id")], mapping it onto
/// Elasticsearch's reserved `_id` metadata field. AutoMap would then emit a `_id` field mapping,
/// which Elasticsearch rejects — so index creation threw on first use. The property now maps to a
/// non-reserved "docKey" field.
/// </summary>
public class ElasticSyncKnowledgeMappingTests
{
    [Fact]
    public void NoProperty_MapsOntoReservedIdField()
    {
        var names = typeof(ElasticSyncKnowledgeItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<ElasticsearchPropertyAttributeBase>())
            .Where(a => a != null)
            .Select(a => a!.Name)
            .ToList();

        names.Should().NotContain("_id", "field names beginning with '_' are reserved and rejected by Elasticsearch");
    }

    [Fact]
    public void Id_MapsTo_DocKey_Keyword()
    {
        var attr = typeof(ElasticSyncKnowledgeItem)
            .GetProperty(nameof(ElasticSyncKnowledgeItem.Id))!
            .GetCustomAttribute<ElasticsearchPropertyAttributeBase>();

        attr.Should().BeOfType<KeywordAttribute>();
        attr!.Name.Should().Be("docKey");
    }

    [Fact]
    public void AutoMap_DoesNotEmit_ReservedIdField()
    {
        // Render the AutoMap for the type offline (no server) and confirm the produced mapping JSON
        // does not declare a `_id` property but does declare the renamed docKey field.
        var client = new ElasticClient(new ConnectionSettings(new Uri("http://localhost:9200")));
        var request = new CreateIndexRequest("sync-knowledge")
        {
            Mappings = new TypeMapping()
        };
        var descriptor = new CreateIndexDescriptor("sync-knowledge")
            .Map<ElasticSyncKnowledgeItem>(m => m.AutoMap());

        var json = client.RequestResponseSerializer.SerializeToString((ICreateIndexRequest)descriptor);

        json.Should().Contain("docKey");
        json.Should().NotContain("\"_id\"");
    }
}
