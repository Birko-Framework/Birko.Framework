using System;
using System.Threading.Tasks;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.RavenDB.IndexManagement;
using FluentAssertions;
using Raven.Client.Documents;
using Xunit;

namespace Birko.Data.RavenDB.Tests.IndexManagement;

/// <summary>TASK-636: RavenDB has no partial index, so a WhereNotNull / WhereNull declaration is refused, never ignored (rule 41).</summary>
public class Task636PredicateRefusalTests : IDisposable
{
    private readonly IDocumentStore _store = new DocumentStore { Urls = new[] { "http://127.0.0.1:1" }, Database = "none" }.Initialize();
    public void Dispose() => _store.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_partial_index_is_refused(bool notNull)
    {
        var definition = new IndexDefinition { Name = "IX_Code", Fields = new[] { IndexField.Ascending("Code") } };
        if (notNull) definition.WhereNotNull = new[] { "Code" }; else definition.WhereNull = new[] { "DeletedAt" };

        await new RavenDBIndexManager(_store).Invoking(m => m.CreateAsync(definition, "Orders"))
            .Should().ThrowAsync<NotSupportedException>().WithMessage("RavenDB has no partial index*");
    }
}
