using System;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>TASK-636: Cosmos DB has no partial index, so a WhereNotNull / WhereNull declaration is refused (rule 41).</summary>
public class Task636PredicateRefusalTests
{
    [Fact]
    public async Task A_partial_index_is_refused_before_the_account_is_asked()
    {
        using var client = new CosmosClient("AccountEndpoint=http://127.0.0.1:1/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");
        var manager = new CosmosDBIndexManager(client.GetDatabase("none"));
        var definition = new IndexDefinition { Name = "IX_Code", Fields = new[] { IndexField.Ascending("Code") }, WhereNotNull = new[] { "Code" } };

        await manager.Invoking(m => m.CreateAsync(definition, "Rows")).Should().ThrowAsync<NotSupportedException>().WithMessage("Cosmos DB has no partial index*");
    }
}
