using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-285 — a <c>COUNT</c> of a table that does not exist returns <b>0</b>, the same answer a
/// <c>SELECT</c> of it already gave, instead of throwing.
///
/// <para><b>What was measured before the fix.</b> The two statement shapes disagreed about the identical
/// condition: a reader caught the missing table and did <c>yield break</c>
/// (<c>AbstractConnector.cs:452</c>, async <c>AbstractAsyncConnector.cs:378</c>) while the scalar
/// <c>count(*)</c> had no exemption, so it reached <c>InitException</c> →
/// <c>EnsureSchemaAndReport</c> and threw. Raised by Symbio TASK-602, where it surfaced as an
/// intermittent <b>500</b> on a first read after a fresh deployment — roughly 1 bring-up in 5.</para>
///
/// <para>⚠ <b>This does not claim to explain WHY a table was missing.</b> Seven hypotheses were tested
/// against a live system there and none reproduced the condition; that question stays open in TASK-602.
/// What is fixed is that the answer is now correct either way, and consistent between the two shapes.</para>
///
/// <para>⚠ <b>The write half is pinned here deliberately.</b> The obvious over-broad version of this fix
/// is to stop <c>EnsureSchemaAndReport</c> throwing, which would reopen TASK-277 — writes silently
/// discarded, <c>Create</c> returning a real <c>Guid</c> against a table that was never created. The last
/// test in this file exists so that regression is caught by name rather than noticed later.</para>
/// </summary>
public class MissingTableCountEndToEndTests : IDisposable
{
    private readonly string _root;

    public MissingTableCountEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-missingcount-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class RowMapping : IModelMapping<Row>
    {
        public void Configure(ModelMap<Row> map)
        {
            map.ToTable("MissingCountRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private static void Register()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();
    }

    /// <summary>
    /// A connector over a real, empty database file in which the table was deliberately NEVER created.
    /// </summary>
    /// <remarks>
    /// ⚠ Driven at the CONNECTOR, not through a store. A store's public CRUD funnels through
    /// <c>EnsureInitializedAsync</c>, which creates the table first — measured in Symbio, where a
    /// count-only route against a cold table returns 200 for exactly that reason. Going through a store
    /// would therefore assert nothing: the condition under test would never arise.
    /// </remarks>
    private SqLiteConnector ConnectorWithNoTable()
    {
        Register();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "missingcount.db" });
        return (SqLiteConnector)factory.GetConnector();
    }

    // ── the defect ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectCount_OfAMissingTable_IsZeroRatherThanAThrow()
    {
        var connector = ConnectorWithNoTable();

        var count = connector.SelectCount(typeof(Row));

        count.Should().Be(0,
            "the count of a table that does not exist is 0 — before TASK-285 this threw, while a SELECT "
            + "of the same table already returned an empty result");
    }

    [Fact]
    public async Task SelectCountAsync_OfAMissingTable_IsZeroRatherThanAThrow()
    {
        var connector = ConnectorWithNoTable();

        var count = await connector.SelectCountAsync(
            typeof(Row), (System.Collections.Generic.IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null,
            CancellationToken.None);

        count.Should().Be(0, "the async half must agree with the sync half — a fix to one of the two is "
            + "the recurring shape of defect in this area");
    }

    // ── the reason the fix needs the inner chain ─────────────────────────────────────────────────────

    [Fact]
    public void TheMissingTablePredicate_MustWalkTheInnerChain_BecauseTheOuterMessageIsTheSql()
    {
        var connector = ConnectorWithNoTable();

        // Exactly the shape EnsureSchemaAndReport rethrows: the SQL as the message, the provider's own
        // error one level down.
        var wrapped = new Exception(
            "SELECT count(*) as count FROM \"MissingCountRows\" AS MissingCountRows",
            new Exception("SQLite Error 1: 'no such table: MissingCountRows'."));

        connector.IsMissingTableException(wrapped).Should().BeFalse(
            "the direct predicate reads ex.Message only — this is not a bug in it, it is why the chain "
            + "walk has to exist");
        connector.IsMissingTableExceptionChain(wrapped).Should().BeTrue(
            "a catch built on the direct predicate would compile, run, and silently never match, so the "
            + "fix would look applied while changing nothing");
    }

    [Fact]
    public void TheChainPredicate_DoesNotMatchAnUnrelatedFailure()
    {
        var connector = ConnectorWithNoTable();

        var unrelated = new Exception("SELECT …", new Exception("SQLite Error 5: 'database is locked'."));

        connector.IsMissingTableExceptionChain(unrelated).Should().BeFalse(
            "a lock, a syntax error or a constraint violation must still surface — swallowing those is "
            + "the failure mode this fix is deliberately narrow to avoid");
    }

    // ── what must NOT change ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACountOfATableThatEXISTS_StillCountsItsRows()
    {
        Register();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "missingcount.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });

        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "missingcount.db"));
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }

        connector.SelectCount(typeof(Row)).Should().Be(3,
            "returning 0 for a MISSING table must not become returning 0 for a present one — without this "
            + "assertion the fix could pass by never counting anything");
    }

    [Fact]
    public void AWriteToAMissingTable_STILL_REPORTS_BecauseTASK277MeasuredThoseBeingDiscarded()
    {
        var connector = ConnectorWithNoTable();

        var act = () => connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "x", Amount = 1 });

        act.Should().Throw<Exception>(
            "TASK-277 measured Create returning a real Guid against a table that was never created. This "
            + "fix is scoped to counts precisely so that stays fixed; widening the catch to writes would "
            + "reopen it silently");
    }
}
