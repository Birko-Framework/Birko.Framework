using Birko.Data.Patterns.IndexManagement;
using Birko.Health;
using Birko.Health.Data.IndexManagement;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Data.IndexManagement.Tests;

/// <summary>
/// TASK-635, offline: <see cref="IndexDeclarationDriftDetector"/> against an in-memory index manager, and the health check
/// over its reports. The providers' own managers are measured live in their test projects.
/// </summary>
public class IndexDeclarationDriftTests
{
    private sealed class FakeManager : IIndexManager
    {
        public Dictionary<string, IndexInfo> Stored { get; } = new();
        public Exception? Throws { get; set; }

        public Task<IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            if (Throws != null) throw Throws;
            return Task.FromResult(Stored.TryGetValue(indexName, out var info) ? info : null);
        }

        public Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default) => throw new NotSupportedException("detection only");
        public Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default) => throw new NotSupportedException("detection only");
        public Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static IndexDefinition Declared(string name, bool unique, params IndexField[] fields) => new() { Name = name, Unique = unique, Fields = fields };
    private static IndexInfo Stored(string name, bool unique, params IndexField[] fields) => new() { Name = name, Unique = unique, Fields = fields };

    [Fact]
    public async Task A_matching_index_is_clean()
    {
        var manager = new FakeManager();
        manager.Stored["UX_Number"] = Stored("UX_Number", true, IndexField.Ascending("Number"));

        var report = await IndexDeclarationDriftDetector.DetectAsync(manager, new[] { Declared("UX_Number", true, IndexField.Ascending("Number")) }, "Invoices");

        report.IsClean.Should().BeTrue();
        report.Declared.Should().Be(1);
    }

    [Fact]
    public async Task Each_kind_is_reported()
    {
        var manager = new FakeManager();
        manager.Stored["IX_Order"] = Stored("IX_Order", false, IndexField.Ascending("B"), IndexField.Ascending("A"));
        manager.Stored["IX_Dir"] = Stored("IX_Dir", false, IndexField.Ascending("A"), IndexField.Ascending("B"));
        manager.Stored["UX_Plain"] = Stored("UX_Plain", false, IndexField.Ascending("Code"));

        var report = await IndexDeclarationDriftDetector.DetectAsync(manager, new[]
        {
            Declared("IX_Missing", false, IndexField.Ascending("X")),
            Declared("IX_Order", false, IndexField.Ascending("A"), IndexField.Ascending("B")),
            Declared("IX_Dir", false, IndexField.Ascending("A"), IndexField.Descending("B")),
            Declared("UX_Plain", true, IndexField.Ascending("Code")),
        }, "Invoices");

        report.Drifts.Select(d => (d.Index, d.Kind)).Should().BeEquivalentTo(new[]
        {
            ("IX_Missing", IndexDeclarationDriftKind.Missing),
            ("IX_Order", IndexDeclarationDriftKind.ColumnsDiffer),
            ("IX_Dir", IndexDeclarationDriftKind.ColumnsDiffer),
            ("UX_Plain", IndexDeclarationDriftKind.UniquenessDiffers),
        });
        report.Drifts.Single(d => d.Index == "UX_Plain").ToString().Should().Contain("UNIQUE (Code)").And.Contain("stored (Code)");
    }

    [Fact]
    public async Task An_index_whose_fields_the_store_does_not_report_is_not_counted_clean()
    {
        var manager = new FakeManager();
        manager.Stored["IX_Custom"] = Stored("IX_Custom", false);

        var report = await IndexDeclarationDriftDetector.DetectAsync(manager, new[] { Declared("IX_Custom", false, IndexField.Ascending("A")) }, "Rows");

        report.IsClean.Should().BeFalse();
        report.Unverified.Should().ContainSingle().Which.Reason.Should().Contain("no fields");
    }

    [Fact]
    public async Task A_lookup_that_throws_is_not_compared_and_says_why()
    {
        var manager = new FakeManager { Throws = new InvalidOperationException("container gone") };

        var report = await IndexDeclarationDriftDetector.DetectAsync(manager, new[] { Declared("IX", false, IndexField.Ascending("A")) }, "Rows");

        report.Unverified.Should().ContainSingle().Which.Reason.Should().Contain("container gone");
    }

    [Fact]
    public async Task The_health_check_is_Degraded_for_drift_and_for_an_uncompared_index_and_Healthy_only_when_all_match()
    {
        static Func<CancellationToken, Task<IndexDeclarationReport>> Probe(IndexDeclarationReport r) => _ => Task.FromResult(r);
        var clean = new IndexDeclarationReport("A", 2, Array.Empty<IndexDeclarationDrift>(), Array.Empty<IndexDeclarationUnverified>());
        var drifted = new IndexDeclarationReport("B", 1,
            new[] { new IndexDeclarationDrift("B", "UX", IndexDeclarationDriftKind.Missing, "UNIQUE (Code)", null) }, Array.Empty<IndexDeclarationUnverified>());
        var uncompared = new IndexDeclarationReport("C", 1, Array.Empty<IndexDeclarationDrift>(), new[] { new IndexDeclarationUnverified("C", "IX", "no fields") });

        (await new IndexDeclarationDriftHealthCheck(new[] { Probe(clean) }).CheckAsync()).Status.Should().Be(HealthStatus.Healthy);
        var withDrift = await new IndexDeclarationDriftHealthCheck(new[] { Probe(clean), Probe(drifted) }).CheckAsync();
        withDrift.Status.Should().Be(HealthStatus.Degraded);
        ((IEnumerable<string>)withDrift.Data!["drift"]).Should().ContainSingle().Which.Should().Contain("B.UX");
        (await new IndexDeclarationDriftHealthCheck(new[] { Probe(clean), Probe(uncompared) }).CheckAsync()).Status.Should().Be(HealthStatus.Degraded);
        (await new IndexDeclarationDriftHealthCheck(new Func<CancellationToken, Task<IndexDeclarationReport>>[] { _ => throw new TimeoutException("down") })
            .CheckAsync()).Status.Should().Be(HealthStatus.Unhealthy);
    }
}
