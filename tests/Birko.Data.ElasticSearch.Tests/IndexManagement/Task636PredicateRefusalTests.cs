using System;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement;

/// <summary>TASK-636: ElasticSearch has no partial index, so a WhereNotNull / WhereNull declaration is refused (rule 41).</summary>
public class Task636PredicateRefusalTests
{
    [Fact]
    public async Task A_partial_index_is_refused_before_the_cluster_is_asked()
    {
        var adapter = new ElasticSearchIndexManagerAdapter(new ElasticClient(new ConnectionSettings(new Uri("http://127.0.0.1:1"))));
        var definition = new IndexDefinition { Name = "orders", Fields = new[] { Birko.Data.Patterns.IndexManagement.IndexField.Ascending("Code") }, WhereNull = new[] { "DeletedAt" } };

        await adapter.Invoking(a => a.CreateAsync(definition)).Should().ThrowAsync<NotSupportedException>().WithMessage("ElasticSearch has no partial index*");
    }
}
