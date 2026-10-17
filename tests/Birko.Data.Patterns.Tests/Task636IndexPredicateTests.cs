using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>TASK-636: index predicates are coherent, refusable, and compared for drift.</summary>
public class Task636IndexPredicateTests
{
    private sealed class OneIndexManager : IIndexManager
    {
        private readonly IndexInfo? _info;
        public OneIndexManager(IndexInfo? info) => _info = info;
        public Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default) => Task.FromResult(_info != null);
        public Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IndexInfo>>(_info == null ? Array.Empty<IndexInfo>() : new[] { _info });
        public Task<IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default) => Task.FromResult(_info);
    }

    private static readonly IndexField[] Marker = { IndexField.Ascending("DefaultMarker") };

    private static IndexDefinition Declared(params string[] whereNotNull) => new()
    {
        Name = "UX_Default", Fields = Marker, Unique = true, WhereNotNull = whereNotNull,
    };

    private static IndexInfo Stored(IReadOnlyList<string>? whereNotNull, IReadOnlyList<string>? whereNull) => new()
    {
        Name = "UX_Default", Fields = Marker, Unique = true, WhereNotNull = whereNotNull, WhereNull = whereNull,
    };

    [Fact]
    public async Task The_same_predicate_is_clean_in_any_order()
    {
        var declared = new IndexDefinition { Name = "UX_Default", Fields = Marker, Unique = true, WhereNotNull = new[] { "A", "B" } };

        (await IndexDeclarationDriftDetector.DetectAsync(new OneIndexManager(Stored(new[] { "B", "A" }, Array.Empty<string>())), new[] { declared }))
            .IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task A_full_index_where_a_partial_one_is_declared_is_predicate_drift_and_the_reverse_too()
    {
        var full = await IndexDeclarationDriftDetector.DetectAsync(new OneIndexManager(Stored(Array.Empty<string>(), Array.Empty<string>())), new[] { Declared("DefaultMarker") });
        var partial = await IndexDeclarationDriftDetector.DetectAsync(new OneIndexManager(Stored(new[] { "DefaultMarker" }, Array.Empty<string>())), new[] { Declared() });

        full.Drifts.Should().ContainSingle(d => d.Kind == IndexDeclarationDriftKind.PredicateDiffers)
            .Which.Declared.Should().Be("UNIQUE (DefaultMarker) WHERE DefaultMarker IS NOT NULL");
        partial.Drifts.Should().ContainSingle(d => d.Kind == IndexDeclarationDriftKind.PredicateDiffers);
    }

    [Fact]
    public async Task A_manager_that_does_not_report_predicates_cannot_confirm_a_declared_one()
    {
        var report = await IndexDeclarationDriftDetector.DetectAsync(new OneIndexManager(Stored(null, null)), new[] { Declared("DefaultMarker") });
        var plain = await IndexDeclarationDriftDetector.DetectAsync(new OneIndexManager(Stored(null, null)), new[] { Declared() });

        report.Drifts.Should().BeEmpty();
        report.Unverified.Should().ContainSingle().Which.Reason.Should().Contain("does not report index predicates");
        plain.IsClean.Should().BeTrue("a declaration without a predicate needs nothing confirmed");
    }

    [Fact]
    public async Task An_unrecognized_stored_predicate_is_unverified_not_compared()
    {
        var stored = Stored(Array.Empty<string>(), Array.Empty<string>());
        stored.UnrecognizedPredicate = "{ \"Qty\" : { \"$gt\" : 5 } }";

        var report = await IndexDeclarationDriftDetector.DetectAsync(new OneIndexManager(stored), new[] { Declared() });

        report.Unverified.Should().ContainSingle().Which.Reason.Should().Contain("$gt");
    }

    [Fact]
    public void A_contradictory_or_empty_predicate_is_refused_and_RefusePredicates_names_the_provider()
    {
        var both = new IndexDefinition { Name = "IX", Fields = Marker, WhereNotNull = new[] { "A" }, WhereNull = new[] { "A" } };
        var empty = new IndexDefinition { Name = "IX", Fields = Marker, WhereNull = new[] { " " } };

        both.Invoking(d => d.RequireCoherentPredicates()).Should().Throw<ArgumentException>().WithMessage("*both*");
        empty.Invoking(d => d.RequireCoherentPredicates()).Should().Throw<ArgumentException>();
        Declared("DefaultMarker").Invoking(d => d.RefusePredicates("RavenDB")).Should().Throw<NotSupportedException>().WithMessage("RavenDB*");
        Declared().Invoking(d => d.RefusePredicates("RavenDB")).Should().NotThrow();
    }
}
