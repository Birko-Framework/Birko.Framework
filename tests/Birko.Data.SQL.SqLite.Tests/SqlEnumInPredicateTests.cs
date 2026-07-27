using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// Regression: <c>enumSet.Contains(x.EnumColumn)</c> must translate to an <c>IN</c> list that actually
/// matches the column. Measured in a consumer (Symbio TASK-249/TASK-254):
/// <c>CountAsync(o =&gt; statuses.Contains(o.Status))</c> returned 0 against 21 matching rows, with the set
/// as a <c>static readonly</c> field and as a captured local alike — a silent WRONG-ROWS result, the
/// worst class of translation bug.
///
/// Cause: on .NET 9+ an ARRAY <c>set.Contains(x.Col)</c> binds to
/// <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T, IEqualityComparer&lt;T&gt;?)</c> whenever T is
/// not <c>IEquatable&lt;T&gt;</c> — true for every enum and nullable enum. The parser fed that trailing
/// <c>null</c> comparer into the condition as a value, which took the constant-null path and turned the
/// whole condition into <c>IS NULL</c>. Guid/int/string sets ARE <c>IEquatable</c> and bind the 2-argument
/// overload, which is why the canonical N+1 batch pattern never exposed it. Fixed by
/// <c>DataBase.IsNonOperandArgument</c>; <c>AbstractConnectorBase.NormalizeParameterValue</c> additionally
/// binds enums as their underlying integer so no provider has to guess.
///
/// The asymmetry that let this survive review: enum EQUALITY works, because the C# compiler lifts
/// <c>x.Status == Foo</c> to the underlying integral type inside the expression tree.
///
/// These cases run against a real SQLite file and are compared against a compiled-delegate oracle. An
/// in-memory-store test would be worthless here: it COMPILES the lambda, so it passes on a predicate
/// whose SQL translation is broken — which is precisely why this reached production.
/// </summary>
public class SqlEnumInPredicateTests : IDisposable
{
    private readonly string _root;

    public SqlEnumInPredicateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sql-enumin-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public enum OrderState
    {
        Created = 0,
        Confirmed = 1,
        Paid = 2,
        Processing = 3,
        Shipped = 4,
        Cancelled = 5,
    }

    public class ERow : AbstractModel
    {
        public string? Name { get; set; }
        public OrderState State { get; set; }
        public OrderState? Fallback { get; set; }
    }

    private sealed class ERowMapping : IModelMapping<ERow>
    {
        public void Configure(ModelMap<ERow> map)
        {
            map.ToTable("ERows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.State);
            map.Property(x => x.Fallback);
        }
    }

    private AsyncSQLiteStore<ERow> NewStore()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new ERowMapping());
        registry.ApplyToDatabase();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "enumin.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(ERow) });
        var store = new AsyncSQLiteStore<ERow>();
        store.SetSettings(new SqLiteSettings(_root, "enumin.db"));
        return store;
    }

    private static List<ERow> Seed() => new()
    {
        new ERow { Name = "a", State = OrderState.Created,    Fallback = OrderState.Paid },
        new ERow { Name = "b", State = OrderState.Confirmed,  Fallback = null },
        new ERow { Name = "c", State = OrderState.Paid,       Fallback = OrderState.Cancelled },
        new ERow { Name = "d", State = OrderState.Processing, Fallback = OrderState.Paid },
        new ERow { Name = "e", State = OrderState.Shipped,    Fallback = null },
        new ERow { Name = "f", State = OrderState.Cancelled,  Fallback = OrderState.Created },
    };

    private static readonly OrderState[] OpenStates =
        [OrderState.Created, OrderState.Confirmed, OrderState.Paid, OrderState.Processing];

    public static IEnumerable<object[]> PredicateCases()
    {
        var captured = new[] { OrderState.Created, OrderState.Confirmed, OrderState.Paid, OrderState.Processing };
        var asList = new List<OrderState> { OrderState.Shipped, OrderState.Cancelled };
        var single = new[] { OrderState.Paid };
        var nullableSet = new OrderState?[] { OrderState.Paid, OrderState.Cancelled };

        var cases = new (string label, Expression<Func<ERow, bool>> expr, int expected)[]
        {
            // The exact shape that returned 0 rows before the fix — static readonly field.
            ("staticReadonlyArray", x => OpenStates.Contains(x.State), 4),
            // …and the "hoist it to a local" remedy that did not help either.
            ("capturedLocalArray",  x => captured.Contains(x.State), 4),
            ("capturedList",        x => asList.Contains(x.State), 2),
            ("singleElement",       x => single.Contains(x.State), 1),
            // Leaf negation → NOT IN.
            ("negatedIn",           x => !OpenStates.Contains(x.State), 2),
            // IN combined with another safe construct.
            ("inAndEquality",       x => OpenStates.Contains(x.State) && x.Name == "c", 1),
            ("inOrEquality",        x => asList.Contains(x.State) || x.State == OrderState.Created, 3),
            // Nullable enum column: NULL rows must not match, exactly as C# semantics say.
            // Rows a + d (Paid) and c (Cancelled) match; the two NULL Fallback rows must not.
            ("nullableColumn",      x => nullableSet.Contains(x.Fallback), 3),
            // Enum equality must keep working (it always did — guard against a regression).
            ("equalityStillWorks",  x => x.State == OrderState.Paid, 1),
        };
        foreach (var c in cases)
            yield return new object[] { c.label, c.expr, c.expected };
    }

    [Theory]
    [MemberData(nameof(PredicateCases))]
    public async Task EnumPredicate_MatchesCompiledDelegateOracle(string label, Expression<Func<ERow, bool>> expr, int expected)
    {
        var store = NewStore();
        await store.CreateAsync(Seed());
        var all = (await store.ReadAsync(x => true)).ToList();

        var oracle = all.Where(expr.Compile()).Select(r => r.Guid).OrderBy(g => g).ToList();
        var sql = (await store.ReadAsync(expr)).Select(r => r.Guid).OrderBy(g => g).ToList();

        // Assert the expected count explicitly as well as the oracle: an oracle-only assertion would
        // also pass if BOTH sides were empty, which is the very failure mode under test.
        oracle.Should().HaveCount(expected, $"the case '{label}' must be a non-trivial fixture");
        sql.Should().Equal(oracle, $"SQL translation must match C# semantics for case '{label}'");
    }

    [Fact]
    public async Task EnumIn_Count_MatchesRowCount()
    {
        // The consumer-side repro shape: a COUNT over an enum IN list. Returned 0 before the fix.
        var store = NewStore();
        await store.CreateAsync(Seed());

        var count = await store.CountAsync(x => OpenStates.Contains(x.State));

        count.Should().Be(4);
    }

    [Fact]
    public async Task EnumValue_InUpdateSet_RoundTripsAsInteger()
    {
        // The write-side half of the same defect: an enum bound into UPDATE … SET goes through the
        // provider's AddParameter directly (no condition strategy), so it needed the same conversion.
        var store = NewStore();
        await store.CreateAsync(Seed());

        await store.UpdateAsync(x => x.Name == "a", new PropertyUpdate<ERow>().Set(x => x.State, OrderState.Shipped));

        var updated = (await store.ReadAsync(x => x.Name == "a")).Single();
        updated.State.Should().Be(OrderState.Shipped);
        // Re-query THROUGH SQL on the new value — proves the stored form is the integer the column holds.
        (await store.CountAsync(x => x.State == OrderState.Shipped)).Should().Be(2);
    }
}
