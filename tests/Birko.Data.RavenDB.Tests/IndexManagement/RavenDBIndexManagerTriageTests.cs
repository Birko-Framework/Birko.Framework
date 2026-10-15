using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.RavenDB.IndexManagement;
using FluentAssertions;
using Raven.Client.Documents;
using Xunit;

namespace Birko.Data.RavenDB.Tests.IndexManagement;

/// <summary>
/// SH-M432 (TASK-154). Every check below runs before any request, so the store points at a port nothing listens on:
/// a request reaching it would fail with a connection error, not with the refusal asserted.
/// </summary>
public class RavenDBIndexManagerTriageTests : IDisposable
{
    private readonly IDocumentStore _store = new DocumentStore { Urls = new[] { "http://127.0.0.1:1" }, Database = "none" }.Initialize();

    public void Dispose() => _store.Dispose();

    private static IndexDefinition Definition(string field = "Code") =>
        new() { Name = "Orders_ByCode", Fields = new List<IndexField> { new() { Name = field } } };

    [Theory]
    [InlineData("unique")]
    [InlineData("sparse")]
    [InlineData("ttl")]
    public async Task A_flag_a_RavenDB_index_cannot_honour_is_refused(string flag)
    {
        var definition = Definition();
        if (flag == "unique") definition.Unique = true;
        else if (flag == "sparse") definition.Sparse = true;
        else definition.ExpireAfter = TimeSpan.FromDays(1);

        await new RavenDBIndexManager(_store).Invoking(m => m.CreateAsync(definition, "Orders")).Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData("Code } select new { Evil = 1", "Orders")]
    [InlineData("Code", "Orders.Where(x => true)")]
    public async Task A_name_that_is_not_an_identifier_never_reaches_the_generated_map(string field, string collection)
        => await new RavenDBIndexManager(_store).Invoking(m => m.CreateAsync(Definition(field), collection))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*not a plain identifier*");
}
