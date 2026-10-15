using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;
using IndexDefinition = Birko.Data.Patterns.IndexManagement.IndexDefinition;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-635, live against the Cosmos emulator: declared indexes compared with what <see cref="CosmosDBIndexManager"/>
/// reports through its name registry (TASK-628). Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class IndexDeclarationDriftLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;

    public IndexDeclarationDriftLiveTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task635_" + Guid.NewGuid().ToString("N"))).Database;
        await _db.CreateContainerIfNotExistsAsync("Invoices", "/id");
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static readonly IndexDefinition[] Declared =
    {
        new() { Name = "IX_Code", Fields = new[] { IndexField.Ascending("Code") } },
        new() { Name = "IX_AB", Fields = new[] { IndexField.Ascending("A"), IndexField.Descending("B") } },
    };

    [Fact]
    public async Task Indexes_created_from_the_declarations_are_clean_and_drift_is_reported()
    {
        if (_db == null) return;
        var manager = new CosmosDBIndexManager(_db);
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
    public async Task A_policy_path_no_name_was_registered_for_is_reported_as_not_compared()
    {
        if (_db == null) return;
        var manager = new CosmosDBIndexManager(_db);
        var policy = await manager.GetIndexingPolicyAsync("Invoices");
        policy.IncludedPaths.Add(new IncludedPath { Path = "/Legacy/?" });
        await manager.SetIndexingPolicyAsync("Invoices", policy);

        var report = await IndexDeclarationDriftDetector.DetectAsync(manager,
            new[] { new IndexDefinition { Name = "Legacy", Fields = new[] { IndexField.Ascending("Legacy") } } }, "Invoices");

        report.Drifts.Should().BeEmpty();
        report.Unverified.Should().ContainSingle().Which.Reason.Should().Contain("no fields");
    }
}
