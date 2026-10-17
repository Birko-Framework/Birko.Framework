using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Paging;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>TASK-651: the paging order's Guid key ignores case; text fields drift as a set.</summary>
public class Task651PagingAndDriftTests
{
    public class Row : AbstractModel { public string? Name { get; set; } }

    [Fact]
    public void A_Guid_key_named_in_another_case_is_not_appended_again()
    {
        var order = OrderBy<Row>.ByName("guid");

        PagingOrder.Total(order).Fields.Select(f => f.PropertyName).Should().Equal("guid");
    }

    private sealed class StoredText : IIndexManager
    {
        public Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IndexInfo>>(new IndexInfo[0]);
        public Task<IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default)
            => Task.FromResult<IndexInfo?>(new IndexInfo { Name = indexName, Fields = new[] { IndexField.Text("body"), IndexField.Text("title") } });
    }

    [Fact]
    public async Task Text_fields_read_back_in_the_servers_order_are_not_drift()
    {
        // Measured on MongoDB 7: a text index declared { title, body } reads back its weights as { body, title }.
        var declared = new IndexDefinition { Name = "IX_Search", Fields = new[] { IndexField.Text("title"), IndexField.Text("body") } };

        (await IndexDeclarationDriftDetector.DetectAsync(new StoredText(), new[] { declared })).IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_text_field_is_still_drift()
    {
        var declared = new IndexDefinition { Name = "IX_Search", Fields = new[] { IndexField.Text("title"), IndexField.Text("summary") } };

        (await IndexDeclarationDriftDetector.DetectAsync(new StoredText(), new[] { declared })).Drifts
            .Should().ContainSingle(d => d.Kind == IndexDeclarationDriftKind.ColumnsDiffer);
    }
}
