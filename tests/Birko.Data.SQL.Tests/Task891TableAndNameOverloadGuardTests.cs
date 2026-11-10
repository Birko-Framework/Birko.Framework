using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.Exceptions;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-891 — the connector overloads that take a resolved <see cref="Tables.Table"/> or a table name refuse a column the
/// provider rejects, before they open a connection, sync and async: a <c>Table</c> through its columns (the check the typed
/// overloads run, TASK-872), a table name through the columns the statement writes and the bare ones its filter compares.
/// <para>
/// Before, measured live on all four providers (the live half is <c>RejectedColumnNameContract</c>): with
/// <c>global::Birko.Data.SQL.DataBase.LoadTable(t)</c> or the table's name, <c>Delete</c> deleted 2 of 2 rows and every <c>Update</c> rewrote 2 of
/// 2, because the WHERE named the column bare and the server read its value function. The fake connector's
/// <c>CreateConnection</c> throws <see cref="InvalidOperationException"/>, so an unguarded overload fails with that (or the
/// transaction wrapper's bare <see cref="Exception"/>), never with <see cref="NotSupportedException"/>.
/// </para>
/// </summary>
public class Task891TableAndNameOverloadGuardTests
{
    [Table("T891GuardRows")]
    public class GuardRow : AbstractModel
    {
        public int Order { get; set; }
        public int Amount { get; set; }
    }

    [Table("T891GuardCleanRows")]
    public class GuardCleanRow : AbstractModel
    {
        public int Amount { get; set; }
    }

    /// <summary>A model with no table: an aggregate over it throws instead of answering nothing (rule 53).</summary>
    public class NoTableRow : AbstractModel
    {
        public int Amount { get; set; }
    }

    private sealed class FakeConnector : AbstractAsyncConnector
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        protected override RejectedColumnNames? ColumnNameRefusals { get; } = new("FakeSQL", "test", new[] { "ORDER" });

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new InvalidOperationException("no database in this test");
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field) => "INT";
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field) => field.Name + " INT";
    }

    private static async Task Drain<TItem>(IAsyncEnumerable<TItem> items)
    {
        await foreach (var _ in items) { }
    }

    private const string Rows = "T891GuardRows";

    /// <summary>Every overload taking a <see cref="Tables.Table"/>, as a call on the table of <paramref name="t"/>.</summary>
    private static IEnumerable<(string Name, Func<FakeConnector, Type, Task> Call)> TableOverloads()
    {
        Expression<Func<GuardRow, bool>> filter = x => x.Amount == 1;
        var conditions = () => global::Birko.Data.SQL.DataBase.ParseConditionExpression(filter).ToList();
        var model = new GuardRow { Guid = Guid.NewGuid(), Order = 1, Amount = 2 };
        object Model(Type t) => t == typeof(GuardRow) ? model : new GuardCleanRow { Guid = Guid.NewGuid(), Amount = 2 };
        var setValue = new Dictionary<Expression<Func<GuardRow, int>>, object> { { x => x.Amount, 3 } };
        var setExpr = new Dictionary<Expression<Func<GuardRow, int>>, Expression<Func<GuardRow, int>>> { { x => x.Amount, x => x.Amount + 1 } };
        var values = new Dictionary<string, object> { { "Amount", 3 } };
        Tables.Table T(Type t) => global::Birko.Data.SQL.DataBase.LoadTable(t);

        yield return ("Delete(Table)", (c, t) => { c.Delete(T(t), conditions()); return Task.CompletedTask; });
        yield return ("Insert(Table, model)", (c, t) => { c.Insert(T(t), Model(t)); return Task.CompletedTask; });
        yield return ("Insert(Table, IDictionary)", (c, t) => { c.Insert(T(t), (IDictionary<string, object>)values); return Task.CompletedTask; });
        yield return ("Insert(Table, models)", (c, t) => { c.Insert(T(t), (IEnumerable<object>)new[] { Model(t) }); return Task.CompletedTask; });
        yield return ("Insert(Table, rows)", (c, t) => { c.Insert(T(t), (IEnumerable<IDictionary<string, object>>)new[] { values }); return Task.CompletedTask; });
        yield return ("Update(Table, model)", (c, t) => { c.Update(T(t), Model(t), conditions()); return Task.CompletedTask; });
        yield return ("Update<T,P>(Table, values)", (c, t) => { c.Update(T(t), setValue, conditions()); return Task.CompletedTask; });
        yield return ("Update<T,P>(Table, expressions)", (c, t) => { c.Update(T(t), setExpr, conditions()); return Task.CompletedTask; });
        yield return ("Update(Table, IDictionary)", (c, t) => { c.Update(T(t), values, conditions()); return Task.CompletedTask; });
        yield return ("SelectCount(Tables)", (c, t) => { c.SelectCount(new[] { T(t) }, conditions()); return Task.CompletedTask; });
        yield return ("Select(Tables)", (c, t) => { _ = c.Select(new[] { T(t) }, null, conditions()).ToList(); return Task.CompletedTask; });

        yield return ("DeleteAsync(Table)", (c, t) => c.DeleteAsync(T(t), conditions()));
        yield return ("InsertAsync(Table, model)", (c, t) => c.InsertAsync(T(t), Model(t)));
        yield return ("InsertAsync(Table, IDictionary)", (c, t) => c.InsertAsync(T(t), (IDictionary<string, object>)values));
        yield return ("InsertAsync(Table, models)", (c, t) => c.InsertAsync(T(t), (IEnumerable<object>)new[] { Model(t) }));
        yield return ("InsertAsync(Table, rows)", (c, t) => c.InsertAsync(T(t), (IEnumerable<IDictionary<string, object>>)new[] { values }));
        yield return ("UpdateAsync(Table, model)", (c, t) => c.UpdateAsync(T(t), Model(t), conditions()));
        yield return ("UpdateAsync<T,P>(Table, values)", (c, t) => c.UpdateAsync(T(t), setValue, conditions()));
        yield return ("UpdateAsync<T,P>(Table, expressions)", (c, t) => c.UpdateAsync(T(t), setExpr, conditions()));
        yield return ("UpdateAsync(Table, IDictionary)", (c, t) => c.UpdateAsync(T(t), values, conditions()));
        yield return ("SelectCountAsync(Tables)", (c, t) => c.SelectCountAsync(new[] { T(t) }, conditions()));
        yield return ("SelectAsync(Tables)", (c, t) => Drain(c.SelectAsync(new[] { T(t) }, null, conditions())));
    }

    /// <summary>Every overload taking a table name, as a call that names the column <paramref name="column"/>.</summary>
    private static IEnumerable<(string Name, Func<FakeConnector, string, Task> Call)> NameOverloads()
    {
        List<Condition> Filter(string column) => new() { new Condition(column, new object[] { 1 }) };
        List<Condition> Nested(string column) => new() { new Condition(null, null, subConditions: new[] { new Condition("Amount", new object[] { 1 }), new Condition(column, new object[] { 2 }, isOr: true) }) };
        var amount = new Dictionary<string, object> { { "Amount", 3 } };
        Dictionary<string, object> Row(string column) => new() { { "Guid", Guid.NewGuid() }, { column, 1 } };
        var setValue = new Dictionary<Expression<Func<GuardCleanRow, int>>, object> { { x => x.Amount, 3 } };
        var setExpr = new Dictionary<Expression<Func<GuardCleanRow, int>>, Expression<Func<GuardCleanRow, int>>> { { x => x.Amount, x => x.Amount + 1 } };
        var setClauses = (string column) => { var s = new SetClauses(); s.Add(0, column, column + " + 1"); return s; };

        yield return ("Update(name, fields, values) filter", (c, col) => { c.Update(Rows, new Dictionary<int, string> { { 0, "Amount" } }, amount, Filter(col)); return Task.CompletedTask; });
        yield return ("Update(name, fields, values) nested filter", (c, col) => { c.Update(Rows, new Dictionary<int, string> { { 0, "Amount" } }, amount, Nested(col)); return Task.CompletedTask; });
        yield return ("Update(name, fields, values) SET", (c, col) => { c.Update(Rows, new Dictionary<int, string> { { 0, col } }, new Dictionary<string, object> { { col, 3 } }, Filter("Amount")); return Task.CompletedTask; });
        yield return ("Update(name, SetClauses) SET", (c, col) => { c.Update(Rows, setClauses(col), new Dictionary<string, object>(), Filter("Amount"), isExpressionValues: true); return Task.CompletedTask; });
        yield return ("Update<T,P>(name, values) filter", (c, col) => { c.Update(Rows, setValue, Filter(col)); return Task.CompletedTask; });
        yield return ("Update<T,P>(name, expressions) filter", (c, col) => { c.Update(Rows, setExpr, Filter(col)); return Task.CompletedTask; });
        yield return ("Insert(name, rows)", (c, col) => { c.Insert(Rows, new[] { Row(col) }); return Task.CompletedTask; });
        yield return ("SelectCount(names) filter", (c, col) => { c.SelectCount(new[] { Rows }, Filter(col)); return Task.CompletedTask; });
        yield return ("Select(names, fields) filter", (c, col) => { _ = c.Select(new[] { Rows }, new Dictionary<int, string> { { 0, "Amount" } }, null, Filter(col)).ToList(); return Task.CompletedTask; });
        yield return ("Delete(Table) of a clean table, filter", (c, col) => { c.Delete(global::Birko.Data.SQL.DataBase.LoadTable(typeof(GuardCleanRow)), Filter(col)); return Task.CompletedTask; });

        yield return ("UpdateAsync(name, fields, values) filter", (c, col) => c.UpdateAsync(Rows, new Dictionary<int, string> { { 0, "Amount" } }, amount, Filter(col)));
        yield return ("UpdateAsync(name, fields, values) SET", (c, col) => c.UpdateAsync(Rows, new Dictionary<int, string> { { 0, col } }, new Dictionary<string, object> { { col, 3 } }, Filter("Amount")));
        yield return ("UpdateAsync<T,P>(name, values) filter", (c, col) => c.UpdateAsync(Rows, setValue, Filter(col)));
        yield return ("UpdateAsync<T,P>(name, expressions) filter", (c, col) => c.UpdateAsync(Rows, setExpr, Filter(col)));
        yield return ("InsertAsync(name, rows)", (c, col) => c.InsertAsync(Rows, new[] { Row(col) }));
        yield return ("SelectCountAsync(names) filter", (c, col) => c.SelectCountAsync(new[] { Rows }, Filter(col)));
        yield return ("SelectAsync(names, fields) filter", (c, col) => Drain(c.SelectAsync(new[] { Rows }, new Dictionary<int, string> { { 0, "Amount" } }, null, Filter(col))));
        yield return ("DeleteAsync(Table) of a clean table, filter", (c, col) => c.DeleteAsync(global::Birko.Data.SQL.DataBase.LoadTable(typeof(GuardCleanRow)), Filter(col)));
    }

    public static IEnumerable<object[]> TableOverloadNames => TableOverloads().Select(e => new object[] { e.Name });
    public static IEnumerable<object[]> NameOverloadNames => NameOverloads().Select(e => new object[] { e.Name });

    [Theory]
    [MemberData(nameof(TableOverloadNames))]
    public async Task A_Table_overload_refuses_a_rejected_column_by_name_before_it_opens_a_connection(string name)
    {
        var act = () => TableOverloads().Single(e => e.Name == name).Call(new FakeConnector(), typeof(GuardRow));

        (await act.Should().ThrowAsync<NotSupportedException>(name))
            .WithMessage("Column \"Order\" of entity *GuardRow (property Order, table \"T891GuardRows\") is a reserved word on FakeSQL*");
    }

    [Theory]
    [MemberData(nameof(TableOverloadNames))]
    public async Task A_Table_overload_lets_a_clean_table_through(string name)
    {
        Exception? thrown = null;
        try { await TableOverloads().Single(e => e.Name == name).Call(new FakeConnector(), typeof(GuardCleanRow)); }
        catch (Exception ex) { thrown = ex; }

        thrown.Should().NotBeOfType<NotSupportedException>(name + " must reach the database for a clean table");
    }

    [Theory]
    [MemberData(nameof(NameOverloadNames))]
    public async Task A_table_name_overload_refuses_a_rejected_column_it_names_before_it_opens_a_connection(string name)
    {
        var act = () => NameOverloads().Single(e => e.Name == name).Call(new FakeConnector(), "Order");

        (await act.Should().ThrowAsync<NotSupportedException>(name))
            .WithMessage("Column \"Order\" of table \"T891Guard*\" is a reserved word on FakeSQL*");
    }

    [Theory]
    [MemberData(nameof(NameOverloadNames))]
    public async Task A_table_name_overload_lets_clean_and_qualified_columns_through(string name)
    {
        foreach (var column in new[] { "Amount", "T891GuardRows.Amount", "OtherTable.Order" })
        {
            Exception? thrown = null;
            try { await NameOverloads().Single(e => e.Name == name).Call(new FakeConnector(), column); }
            catch (Exception ex) { thrown = ex; }

            thrown.Should().NotBeOfType<NotSupportedException>($"{name} with {column} must reach the database");
        }
    }

    /// <summary>
    /// The parser qualifies a filter's columns (<c>T891GuardRows.Order</c>) and a write strips its own table's qualifier, so
    /// the server reads the bare name: qualified by the statement's table, the name is refused as the bare one is.
    /// </summary>
    [Fact]
    public async Task A_column_qualified_by_the_statements_table_is_refused_as_the_bare_one_is()
    {
        var filter = new List<Condition> { new("T891GuardRows.Order", new object[] { 1 }) };
        var connector = new FakeConnector();

        FluentActions.Invoking(() => connector.Update(Rows, new Dictionary<int, string> { { 0, "Amount" } }, new Dictionary<string, object> { { "Amount", 3 } }, filter))
            .Should().Throw<NotSupportedException>().WithMessage("Column \"Order\" of table \"T891GuardRows\"*");
        await FluentActions.Awaiting(() => connector.SelectCountAsync(new[] { Rows }, filter))
            .Should().ThrowAsync<NotSupportedException>().WithMessage("Column \"Order\"*");
    }

    [Fact]
    public async Task A_value_fragment_in_a_filter_is_not_taken_for_a_column()
    {
        var fragment = new Condition("Order", new object[] { 1 }) { NameFragment = _ => "Order" };
        Exception? thrown = null;
        try { await new FakeConnector().SelectCountAsync(new[] { Rows }, new[] { fragment }); }
        catch (Exception ex) { thrown = ex; }

        thrown.Should().NotBeOfType<NotSupportedException>("a fragment is rendered by the dialect, not a bare column");
    }

    [Fact]
    public void The_funnels_are_not_virtual()
    {
        foreach (var name in new[] { "RequireUsableTable", "RequireUsableStatementColumns" })
            typeof(AbstractConnectorBase).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                .IsVirtual.Should().BeFalse($"{name} is the check; the list is the override point (rule 12)");
        typeof(AbstractAsyncConnector).GetMethod(nameof(AbstractAsyncConnector.InsertAsync),
                new[] { typeof(string), typeof(IEnumerable<IDictionary<string, object>>), typeof(System.Threading.CancellationToken) })!
            .IsVirtual.Should().BeFalse("an override of the name insert would skip its check (rule 12)");
    }

    /// <summary>Rule 53: <c>SelectAggregate(Async)</c> had a <c>table == null</c> branch that nothing reaches.</summary>
    [Fact]
    public async Task An_aggregate_over_a_type_with_no_table_throws_instead_of_answering_nothing()
    {
        var connector = new FakeConnector();
        var query = new AggregateQuery<NoTableRow>();

        FluentActions.Invoking(() => connector.SelectAggregate(typeof(NoTableRow), query).ToList())
            .Should().Throw<TableAttributeException>();
        await FluentActions.Awaiting(() => Drain(connector.SelectAggregateAsync(typeof(NoTableRow), query)))
            .Should().ThrowAsync<TableAttributeException>();
    }
}
