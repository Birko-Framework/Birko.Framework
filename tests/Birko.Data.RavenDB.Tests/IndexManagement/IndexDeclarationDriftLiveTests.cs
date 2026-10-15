using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.RavenDB.IndexManagement;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;
using IndexDefinition = Birko.Data.Patterns.IndexManagement.IndexDefinition;
using IndexField = Birko.Data.Patterns.IndexManagement.IndexField;

namespace Birko.Data.RavenDB.Tests.IndexManagement;

/// <summary>
/// TASK-635, live RavenDB: declared indexes compared with what <see cref="RavenDBIndexManager"/> reports. RavenDB stores
/// a map, not a field list, so the manager reads the fields back from a map it generated itself; any other map is
/// reported as not compared. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class IndexDeclarationDriftLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    private readonly IDocumentStore? _store;
    private readonly string _db = "birko-task635-" + Guid.NewGuid().ToString("N");

    public IndexDeclarationDriftLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _store = new DocumentStore { Urls = new[] { Url! }, Database = _db }.Initialize();
        _store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(_db)));
    }

    public void Dispose()
    {
        if (_store == null) return;
        try { _store.Maintenance.Server.Send(new DeleteDatabasesOperation(_db, hardDelete: true)); } catch { }
        _store.Dispose();
    }

    private static readonly IndexDefinition[] Declared =
    {
        new() { Name = "IX_Code", Fields = new[] { IndexField.Ascending("Code") } },
        new() { Name = "IX_AB", Fields = new[] { IndexField.Ascending("A"), IndexField.Ascending("B") } },
        new() { Name = "IX_Search", Fields = new[] { IndexField.Text("Name") } },
    };

    [Fact]
    public async Task Indexes_created_from_the_declarations_are_clean_and_drift_is_reported()
    {
        if (_store == null) return;
        var manager = new RavenDBIndexManager(_store);
        foreach (var d in Declared) await manager.CreateAsync(d, "Invoices");

        var clean = await IndexDeclarationDriftDetector.DetectAsync(manager, Declared, "Invoices");
        foreach (var x in clean.Drifts.Select(d => d.ToString()).Concat(clean.Unverified.Select(u => u.ToString()))) _output.WriteLine(x);
        clean.IsClean.Should().BeTrue();

        await manager.DropAsync("IX_AB", "Invoices");
        await manager.DropAsync("IX_Code", "Invoices");
        await manager.CreateAsync(new IndexDefinition { Name = "IX_Code", Fields = new[] { IndexField.Ascending("Other") } }, "Invoices");

        var drifted = await IndexDeclarationDriftDetector.DetectAsync(manager, Declared.Append(
            new IndexDefinition { Name = "UX_Number", Fields = new[] { IndexField.Ascending("Number") }, Unique = true }), "Invoices");

        foreach (var u in drifted.Unverified) _output.WriteLine(u.ToString());
        drifted.Drifts.Select(d => (d.Index, d.Kind)).Should().BeEquivalentTo(new[]
        {
            ("IX_Code", IndexDeclarationDriftKind.ColumnsDiffer),
            ("IX_AB", IndexDeclarationDriftKind.Missing),
            ("UX_Number", IndexDeclarationDriftKind.Missing),
        });
    }

    [Fact]
    public async Task An_index_with_a_map_the_manager_did_not_write_is_reported_as_not_compared()
    {
        if (_store == null) return;
        await _store.Maintenance.SendAsync(new PutIndexesOperation(new Raven.Client.Documents.Indexes.IndexDefinition
        {
            Name = "IX_Custom",
            Maps = { "from doc in docs.Invoices select new { Code = doc.Code.ToUpper() }" }
        }));

        var report = await IndexDeclarationDriftDetector.DetectAsync(new RavenDBIndexManager(_store),
            new[] { new IndexDefinition { Name = "IX_Custom", Fields = new[] { IndexField.Ascending("Code") } } }, "Invoices");

        report.Drifts.Should().BeEmpty();
        report.Unverified.Should().ContainSingle().Which.Index.Should().Be("IX_Custom");
    }

    [Fact]
    public void The_generated_map_reads_back_as_its_fields_and_nothing_else_does()
    {
        RavenDBIndexManager.FieldsOfGeneratedMap(RavenDBIndexManager.GeneratedMap("Invoices", new[] { "A", "B" }))
            .Should().Equal("A", "B");
        RavenDBIndexManager.FieldsOfGeneratedMap("from doc in docs.Invoices select new { A = doc.B }").Should().BeNull();
        RavenDBIndexManager.FieldsOfGeneratedMap("from doc in docs.Invoices select new { A = doc.A.ToUpper() }").Should().BeNull();
    }
}
