using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using PortableQueryMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-837 (FIELD-051): a view read or count whose <c>ids.Contains(v.Col)</c> list is past the provider's parameter
/// limit is split by the same planner as a table read (TASK-833), or refused before anything runs. The connector here
/// is SQLite with a limit of 40 and chunks of 10; the oracle is the same filter through an ordinary SQLite connector on
/// the same file, which sends one statement.
/// </summary>
public sealed class Task837ViewInListChunkingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t837-{Guid.NewGuid():N}");

    public Task837ViewInListChunkingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_root);
    }

    public class T837Person : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class T837Order : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class LineView
    {
        public Guid? OrderId { get; set; }
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
        public string PersonName { get; set; } = string.Empty;
    }

    public class TotalView
    {
        public Guid PersonId { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class PersonMapping : IModelMapping<T837Person>
    {
        public void Configure(ModelMap<T837Person> map)
        {
            map.ToTable("T837Persons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name);
        }
    }

    private sealed class OrderMapping : IModelMapping<T837Order>
    {
        public void Configure(ModelMap<T837Order> map)
        {
            map.ToTable("T837Orders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    /// <summary>SQLite with a limit of 40 parameters and chunks of 10.</summary>
    public sealed class LowLimitSqLiteConnector : SqLiteConnector
    {
        public LowLimitSqLiteConnector(Birko.Configuration.PasswordSettings settings) : base(settings) { }

        public override int MaxCommandParameters => 40;

        public override int PreferredInListChunkSize => 10;
    }

    private sealed class Fixture
    {
        public required LowLimitSqLiteConnector Low { get; init; }
        public required SqLiteConnector Oracle { get; init; }
        public required List<T837Person> Persons { get; init; }
        public required List<T837Order> Orders { get; init; }
        public List<string> Executed { get; } = new();
    }

    /// <summary>30 people with two orders each.</summary>
    private async Task<Fixture> SeedAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PersonMapping());
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();

        var settings = new SqLiteSettings(_root, "views.db");
        var oracle = new SqLiteConnector(settings);
        oracle.CreateTable(new[] { typeof(T837Person), typeof(T837Order) });
        var persons = new List<T837Person>();
        var orders = new List<T837Order>();
        for (var i = 0; i < 30; i++)
        {
            var person = new T837Person { Guid = Guid.NewGuid(), Name = "p" + i.ToString("00") };
            persons.Add(person);
            await oracle.InsertAsync(typeof(T837Person), person);
            for (var j = 1; j <= 2; j++)
            {
                var order = new T837Order { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Amount = i * 10 + j };
                orders.Add(order);
                await oracle.InsertAsync(typeof(T837Order), order);
            }
        }

        var low = new LowLimitSqLiteConnector(settings);
        var fixture = new Fixture { Low = low, Oracle = oracle, Persons = persons, Orders = orders };
        low.OnExecute += text => fixture.Executed.Add(text);
        return fixture;
    }

    private static ViewDefinition Lines(PortableQueryMode mode = PortableQueryMode.OnTheFly)
    {
        var builder = new ViewDefinitionBuilder<LineView>()
            .From<T837Order>()
            .Join<T837Order, T837Person, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<T837Order, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<T837Order, Guid>(o => o.PersonId, v => v.PersonId)
            .Select<T837Order, decimal>(o => o.Amount, v => v.Amount)
            .Select<T837Person, string>(p => p.Name!, v => v.PersonName);
        if (mode != PortableQueryMode.OnTheFly) builder = builder.HasName("T837Lines").HasQueryMode(mode);
        return builder.Build();
    }

    private static ViewDefinition Totals(PortableQueryMode mode = PortableQueryMode.OnTheFly)
    {
        var builder = new ViewDefinitionBuilder<TotalView>()
            .From<T837Order>()
            .Select<T837Order, Guid>(o => o.PersonId, v => v.PersonId)
            .GroupBy<T837Order, Guid>(o => o.PersonId)
            .Sum<T837Order, decimal>(o => o.Amount, v => v.Total);
        if (mode != PortableQueryMode.OnTheFly) builder = builder.HasName("T837Totals").HasQueryMode(mode);
        return builder.Build();
    }

    /// <summary>50 order ids, 5 of them twice, and 5 that match nothing: 60 values, past the limit of 40.</summary>
    private static List<Guid?> OrderIds(Fixture f)
    {
        var ids = f.Orders.Take(50).Select(o => o.Guid).ToList();
        ids.AddRange(ids.Take(5).ToList());
        ids.AddRange(Enumerable.Range(0, 5).Select(_ => (Guid?)Guid.NewGuid()));
        return ids;
    }

    private List<string> Selects(Fixture f) => f.Executed.Where(t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)).ToList();

    // --- read and count equal the single-statement oracle ---

    [Theory]
    [InlineData(PortableQueryMode.OnTheFly)]
    [InlineData(PortableQueryMode.Persistent)]
    public async Task A_view_read_past_the_limit_is_split_and_equals_the_oracle(PortableQueryMode mode)
    {
        var f = await SeedAsync();
        if (mode == PortableQueryMode.Persistent) await new SqlViewManager(f.Oracle).EnsureAsync(Lines(mode));
        var ids = OrderIds(f);

        var oracle = (await new SqlViewStore<LineView>(f.Oracle, Lines(mode)).QueryAsync(v => ids.Contains(v.OrderId))).ToList();
        var chunked = (await new SqlViewStore<LineView>(f.Low, Lines(mode)).QueryAsync(v => ids.Contains(v.OrderId))).ToList();

        oracle.Should().HaveCount(50);
        chunked.Select(v => (v.OrderId, v.PersonId, v.Amount, v.PersonName)).Should()
            .BeEquivalentTo(oracle.Select(v => (v.OrderId, v.PersonId, v.Amount, v.PersonName)));
        var selects = Selects(f);
        selects.Should().HaveCount(6, "55 distinct values in chunks of 10");
        selects.Should().OnlyContain(s => s.Contains(" IN ("), "every chunk carries a non-empty IN");
    }

    [Theory]
    [InlineData(PortableQueryMode.OnTheFly)]
    [InlineData(PortableQueryMode.Persistent)]
    public async Task A_view_count_past_the_limit_is_the_sum_of_the_chunks(PortableQueryMode mode)
    {
        var f = await SeedAsync();
        if (mode == PortableQueryMode.Persistent) await new SqlViewManager(f.Oracle).EnsureAsync(Lines(mode));
        var ids = OrderIds(f);

        var oracle = await new SqlViewStore<LineView>(f.Oracle, Lines(mode)).CountAsync(v => ids.Contains(v.OrderId));
        var chunked = await new SqlViewStore<LineView>(f.Low, Lines(mode)).CountAsync(v => ids.Contains(v.OrderId));

        oracle.Should().Be(50);
        chunked.Should().Be(oracle);
        Selects(f).Should().HaveCount(6);
    }

    [Theory]
    [InlineData(PortableQueryMode.OnTheFly)]
    [InlineData(PortableQueryMode.Persistent)]
    public async Task The_sync_view_read_and_count_split_too(PortableQueryMode mode)
    {
        var f = await SeedAsync();
        if (mode == PortableQueryMode.Persistent) await new SqlViewManager(f.Oracle).EnsureAsync(Lines(mode));
        var ids = OrderIds(f);
        var view = SqlViewTranslator.Translate(Lines(mode));
        var conditions = Parse<LineView>(view, v => ids.Contains(v.OrderId));

        var oracle = ((AbstractConnector)f.Oracle).Select(view, ReadOrderId, conditions).ToList();
        var chunked = ((AbstractConnector)f.Low).Select(view, ReadOrderId, conditions).ToList();
        chunked.Should().BeEquivalentTo(oracle).And.HaveCount(50);
        Selects(f).Should().HaveCount(6);

        f.Executed.Clear();
        ((AbstractConnector)f.Low).SelectCount(view, conditions).Should().Be(((AbstractConnector)f.Oracle).SelectCount(view, conditions));
        Selects(f).Should().HaveCount(6);
    }

    [Fact]
    public async Task A_persistent_grouped_view_filters_its_own_rows_so_it_splits()
    {
        var f = await SeedAsync();
        await new SqlViewManager(f.Oracle).EnsureAsync(Totals(PortableQueryMode.Persistent));
        var ids = f.Persons.Select(p => p.Guid!.Value).Concat(Enumerable.Range(0, 20).Select(_ => Guid.NewGuid())).ToList();

        var oracle = (await new SqlViewStore<TotalView>(f.Oracle, Totals(PortableQueryMode.Persistent)).QueryAsync(v => ids.Contains(v.PersonId))).ToList();
        var chunked = (await new SqlViewStore<TotalView>(f.Low, Totals(PortableQueryMode.Persistent)).QueryAsync(v => ids.Contains(v.PersonId))).ToList();

        oracle.Should().HaveCount(30);
        chunked.Select(v => (v.PersonId, v.Total)).Should().BeEquivalentTo(oracle.Select(v => (v.PersonId, v.Total)));
        (await new SqlViewStore<TotalView>(f.Low, Totals(PortableQueryMode.Persistent)).CountAsync(v => ids.Contains(v.PersonId)))
            .Should().Be(30);
        Selects(f).Should().HaveCount(10, "a read and a count, each in 5 chunks of 10");
    }

    [Fact]
    public async Task Below_the_limit_a_sorted_view_read_stays_one_statement()
    {
        var f = await SeedAsync();
        var ids = f.Orders.Take(20).Select(o => o.Guid).ToList();

        var rows = (await new SqlViewStore<LineView>(f.Low, Lines())
            .QueryAsync(v => ids.Contains(v.OrderId), OrderBy<LineView>.By(v => v.Amount))).ToList();

        rows.Select(v => v.Amount).Should().Equal(f.Orders.Take(20).Select(o => o.Amount).OrderBy(a => a));
        Selects(f).Should().ContainSingle();
    }

    // --- refusals, before anything runs ---

    public static IEnumerable<object[]> Refusals() => new[]
    {
        new object[] { "sorted", "sorted" },
        new object[] { "paged", "paged" },
        new object[] { "or", "OR" },
        new object[] { "not-in", "NOT IN" },
        new object[] { "aggregate-read", "aggregate" },
        new object[] { "aggregate-count", "aggregate" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_view_list_past_the_limit_that_cannot_be_split_is_refused_before_anything_runs(string shape, string reason)
    {
        var f = await SeedAsync();
        var ids = OrderIds(f);
        var personIds = f.Persons.Select(p => p.Guid!.Value).Concat(Enumerable.Range(0, 20).Select(_ => Guid.NewGuid())).ToList();
        var lines = new SqlViewStore<LineView>(f.Low, Lines());
        var totals = new SqlViewStore<TotalView>(f.Low, Totals());

        Func<Task> act = shape switch
        {
            "sorted" => () => lines.QueryAsync(v => ids.Contains(v.OrderId), OrderBy<LineView>.By(v => v.Amount)),
            "paged" => () => lines.QueryAsync(v => ids.Contains(v.OrderId), limit: 100),
            "or" => () => lines.QueryAsync(v => ids.Contains(v.OrderId) || v.Amount > 1000m),
            "not-in" => () => lines.QueryAsync(v => !ids.Contains(v.OrderId)),
            "aggregate-read" => () => totals.QueryAsync(v => personIds.Contains(v.PersonId)),
            "aggregate-count" => () => totals.CountAsync(v => personIds.Contains(v.PersonId)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(reason).And.Contain("TASK-833");
        f.Executed.Should().BeEmpty("the refusal is raised before any statement");
    }

    /// <summary>
    /// TASK-895 (the user's decision, 2026-11-10): a view's string list past the limit is split like a Guid list, since
    /// TASK-873 made it exact. It was refused here.
    /// </summary>
    [Fact]
    public async Task A_view_string_list_past_the_limit_is_split()
    {
        var f = await SeedAsync();
        // 30 names that exist, 25 that do not, 5 repeated, and case and space twins C# does not match.
        var names = Enumerable.Range(0, 55).Select(i => "p" + i.ToString("00")).ToList();
        names.AddRange(names.Take(5).ToList());
        names.AddRange(new[] { "P01", "p02 " });
        var lines = new SqlViewStore<LineView>(f.Low, Lines());
        f.Executed.Clear();

        var rows = (await lines.QueryAsync(v => names.Contains(v.PersonName))).ToList();
        rows.Select(r => r.OrderId).Should().BeEquivalentTo(f.Orders.Select(o => (Guid?)o.Guid)).And.OnlyHaveUniqueItems();
        (await lines.CountAsync(v => names.Contains(v.PersonName))).Should().Be(60);
        Selects(f).Count.Should().BeGreaterThan(2, "57 distinct values are read in chunks");
    }

    [Fact]
    public async Task The_sync_view_read_and_count_refuse_too()
    {
        var f = await SeedAsync();
        var ids = OrderIds(f);
        var view = SqlViewTranslator.Translate(Lines());
        var negated = Parse<LineView>(view, v => !ids.Contains(v.OrderId));

        ((AbstractConnector)f.Low).Invoking(c => c.Select(view, ReadOrderId, negated).ToList())
            .Should().Throw<NotSupportedException>().WithMessage("*NOT IN*");
        ((AbstractConnector)f.Low).Invoking(c => c.SelectCount(view, negated))
            .Should().Throw<NotSupportedException>().WithMessage("*NOT IN*");
        f.Executed.Should().BeEmpty();
    }

    // --- the real provider limit ---

    /// <summary>
    /// The measurement behind the task: SQLite binds at most 32,766 parameters, so a view read with 33,000 ids failed in
    /// the driver ("too many SQL variables") before TASK-837. It is now read in chunks of 1000.
    /// </summary>
    [Fact]
    public async Task A_view_read_past_SQLites_own_limit_returns_the_rows()
    {
        var f = await SeedAsync();
        var ids = f.Orders.Select(o => o.Guid).Concat(Enumerable.Range(0, 33_000).Select(_ => (Guid?)Guid.NewGuid())).ToList();
        var store = new SqlViewStore<LineView>(f.Oracle, Lines());

        (await store.QueryAsync(v => ids.Contains(v.OrderId))).Select(v => v.OrderId).Should()
            .BeEquivalentTo(f.Orders.Select(o => o.Guid));
        (await store.CountAsync(v => ids.Contains(v.OrderId))).Should().Be(60);
    }

    // --- helpers: the sync funnel takes parsed conditions, as SqlViewStore builds them ---

    private static object ReadOrderId(IDictionary<int, string> fields, DbDataReader reader) => reader.GetValue(0);

    private static List<Conditions.Condition> Parse<TView>(Tables.View view, Expression<Func<TView, bool>> filter)
    {
        using var scope = DataBase.ScopeFieldResolver((type, property, withTableName) =>
        {
            if (type != typeof(TView)) return null;
            var field = view.GetTableFields().FirstOrDefault(x => x.Property?.Name == property);
            return field?.GetSelectName(withTableName);
        });
        return DataBase.ParseConditionExpression(filter).ToList();
    }
}
