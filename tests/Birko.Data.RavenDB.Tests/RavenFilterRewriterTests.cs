using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.RavenDB.Expressions;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Xunit;

namespace Birko.Data.RavenDB.Tests;

/// <summary>
/// TASK-221 — RavenDB's LINQ provider translates NO collection <c>Contains</c>. Measured: a baseline
/// <c>x =&gt; x.Amount &gt; 3</c> renders fine, while array, <c>List&lt;T&gt;</c> and the explicit
/// <c>Enumerable.Contains</c> spellings all fail with
/// <c>NotSupportedException: Expression type not supported: TypedParameterExpression</c>. Only Raven's own
/// <c>.In()</c> works. <c>IN</c> is the canonical batch-load pattern, so without the rewrite a filter that
/// works on SQL, ElasticSearch, MongoDB and CosmosDB throws on RavenDB alone.
///
/// <para>
/// Distinct from the .NET 9+ <c>MemoryExtensions</c> binding (TASK-218/220): that one breaks only the
/// array spelling. Here every spelling fails, so that rewrite would have changed nothing.
/// </para>
///
/// <para>
/// Non-gated: <c>DocumentStore.Initialize()</c> and query <i>building</i> are local, so RQL rendering
/// needs no database. The store is pointed at a port nothing listens on and never executes.
/// </para>
/// </summary>
public class RavenFilterRewriterTests : IDisposable
{
    private readonly IDocumentStore _store;

    public RavenFilterRewriterTests()
    {
        _store = new DocumentStore { Urls = new[] { "http://127.0.0.1:65001" }, Database = "spec" };
        _store.Initialize();
    }

    public void Dispose() => _store.Dispose();

    public class Doc : AbstractModel
    {
        public int Amount { get; set; }
        public string? Name { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    private string Rql(Expression<Func<Doc, bool>> filter)
    {
        using var session = _store.OpenSession();
        return session.Query<Doc>().Where(RavenFilterRewriter.Rewrite(filter)!).ToString()!;
    }

    private string RawRql(Expression<Func<Doc, bool>> filter)
    {
        using var session = _store.OpenSession();
        return session.Query<Doc>().Where(filter).ToString()!;
    }

    [Fact]
    public void The_baseline_confirms_RQL_rendering_needs_no_server()
    {
        // Without this, a test that expects a throw proves nothing — it could be the transport.
        RawRql(x => x.Amount > 3).Should().Be("from 'Docs' where Amount > $p0");
    }

    [Theory]
    [MemberData(nameof(PortableSpellings))]
    public void Every_portable_spelling_renders_an_IN(string label, Expression<Func<Doc, bool>> filter)
    {
        Rql(filter).Should().Be("from 'Docs' where Amount in ($p0)", label);
    }

    public static TheoryData<string, Expression<Func<Doc, bool>>> PortableSpellings()
    {
        var arr = new[] { 1, 5 };
        var list = new List<int> { 1, 5 };
        IEnumerable<int> seq = arr;
        return new()
        {
            // The array spelling also carries the MemoryExtensions binding, so this doubles as proof
            // that unwrapping the span conversion — shared with SpanContains — reaches the collection.
            { "int[]", x => arr.Contains(x.Amount) },
            { "List<int>", x => list.Contains(x.Amount) },
            { "IEnumerable<int>", x => seq.Contains(x.Amount) },
            { "Enumerable.Contains", x => Enumerable.Contains(arr, x.Amount) },
        };
    }

    [Theory]
    [MemberData(nameof(PortableSpellings))]
    public void Every_portable_spelling_would_throw_untranslated(string label, Expression<Func<Doc, bool>> filter)
    {
        // Pins the defect, so the theory above cannot quietly become vacuous if Raven starts supporting
        // Contains on its own — this is what would fail first, and it is the signal to delete the rewrite.
        var act = () => RawRql(filter);

        act.Should().Throw<NotSupportedException>(label);
    }

    [Fact]
    public void A_collection_FIELD_holding_a_constant_is_left_strictly_alone()
    {
        // The mirror image, and the reason this rewrite is not a blanket Contains -> In. Membership runs
        // the other way here — "does this entity's collection hold this value" — and Raven ALREADY
        // translates it. Rewriting would break working code, so the rendered RQL must be byte-identical
        // with and without the rewrite.
        Rql(x => x.Tags.Contains("red")).Should().Be(RawRql(x => x.Tags.Contains("red")));
        Rql(x => x.Tags.Contains("red")).Should().Be("from 'Docs' where Tags = $p0");
    }

    [Fact]
    public void The_negated_form_is_rewritten_and_negated()
    {
        var arr = new[] { 1, 5 };

        Rql(x => !arr.Contains(x.Amount)).Should().Contain("not Amount in ($p0)");
    }

    [Fact]
    public void A_membership_test_beside_another_condition_keeps_both()
    {
        var arr = new[] { 1, 5 };

        Rql(x => arr.Contains(x.Amount) && x.Name == "a")
            .Should().Be("from 'Docs' where Amount in ($p0) and Name = $p1");
    }

    [Fact]
    public void A_string_Contains_keeps_Ravens_own_deliberate_refusal()
    {
        // Same method name, entirely different operation — a substring test, not set membership. Raven
        // refuses it ON PURPOSE and says why ("use Search()"), which is more useful than anything a
        // rewrite could produce. So the rewrite must not touch it, and must not swallow or reword the
        // refusal: the outcome has to be identical with and without.
        var withRewrite = Record.Exception(() => Rql(x => x.Name!.Contains("ab")));
        var without = Record.Exception(() => RawRql(x => x.Name!.Contains("ab")));

        withRewrite.Should().BeOfType<NotSupportedException>();
        without.Should().BeOfType<NotSupportedException>();
        withRewrite!.Message.Should().Be(without!.Message);
        withRewrite.InnerException!.Message.Should().Contain("Search()",
            "Raven's own guidance must survive — it names the supported alternative");
    }

    [Fact]
    public void A_predicate_with_no_membership_test_is_returned_unchanged()
    {
        Expression<Func<Doc, bool>> e = x => x.Amount > 3 && x.Name == "a";

        RavenFilterRewriter.Rewrite(e).Should().BeSameAs(e);
    }

    [Fact]
    public void A_null_predicate_stays_null()
    {
        RavenFilterRewriter.Rewrite<Doc>(null).Should().BeNull();
    }
}
