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
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-872 — every typed connector entry point (a method taking the entity <see cref="Type"/>) refuses an entity whose
/// columns include a name the provider rejects, before it opens a connection, sync and async. A store did this already,
/// through its initialisation's <c>CreateTable</c>; a direct connector call never runs that.
/// <para>
/// Before, measured live on all four providers against a table Birko did not build (the live half is
/// <c>RejectedColumnNameContract</c>): <c>Delete</c> and the expression-keyed <c>Update</c> emitted the column bare in
/// their WHERE, so <c>User</c> (PostgreSQL, SQL Server), <c>CURRENT_USER</c> (MySQL) or <c>CURRENT_DATE</c> (SQLite) was
/// read as the session user or the current date, and both wrote 2 of 2 rows that matched nothing in C#, silently.
/// </para>
/// <para>
/// The fake connector's <c>CreateConnection</c> throws <see cref="InvalidOperationException"/>, so an entry point that
/// is not guarded fails with that (or the transaction wrapper's bare <see cref="Exception"/>), never with
/// <see cref="NotSupportedException"/>.
/// </para>
/// </summary>
public class Task872DirectConnectorGuardTests
{
    [Table("T872GuardRows")]
    public class GuardRow : AbstractModel
    {
        public int Order { get; set; }
        public int Amount { get; set; }
    }

    [Table("T872GuardCleanRows")]
    public class GuardCleanRow : AbstractModel
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

    /// <summary>Every typed entry point, by name, as a call on <paramref name="type"/>.</summary>
    private static IEnumerable<(string Name, Func<FakeConnector, Type, Task> Call)> EntryPoints()
    {
        Expression<Func<GuardRow, bool>> filter = x => x.Amount == 1;
        var conditions = () => global::Birko.Data.SQL.DataBase.ParseConditionExpression(filter);
        var model = new GuardRow { Guid = Guid.NewGuid(), Order = 1, Amount = 2 };
        var setValue = new Dictionary<Expression<Func<GuardRow, int>>, object> { { x => x.Amount, 3 } };
        var setExpr = new Dictionary<Expression<Func<GuardRow, int>>, Expression<Func<GuardRow, int>>> { { x => x.Amount, x => x.Amount + 1 } };
        var order = new Dictionary<Expression<Func<GuardRow, int>>, bool> { { x => x.Amount, true } };
        var aggregate = new AggregateQuery<GuardRow>();

        yield return ("Select(type, expr)", (c, t) => { _ = c.Select(t, filter).ToList(); return Task.CompletedTask; });
        yield return ("Select(type, conditions)", (c, t) => { _ = c.Select(t, conditions()).ToList(); return Task.CompletedTask; });
        yield return ("Select<T,P>(type, expr, order)", (c, t) => { _ = c.Select(t, filter, order).ToList(); return Task.CompletedTask; });
        yield return ("Select(types, expr)", (c, t) => { _ = c.Select(new[] { t }, filter).ToList(); return Task.CompletedTask; });
        yield return ("Select(IEnumerable<Type>, conditions)", (c, t) => { _ = c.Select((IEnumerable<Type>)new[] { t }, conditions()).ToList(); return Task.CompletedTask; });
        yield return ("SelectCount(type, expr)", (c, t) => { c.SelectCount(t, filter); return Task.CompletedTask; });
        yield return ("SelectCount(type, conditions)", (c, t) => { c.SelectCount(t, conditions()); return Task.CompletedTask; });
        yield return ("SelectCount(types, expr)", (c, t) => { c.SelectCount(new[] { t }, filter); return Task.CompletedTask; });
        yield return ("Insert(type, model)", (c, t) => { c.Insert(t, model); return Task.CompletedTask; });
        yield return ("Insert(model)", (c, t) => { c.Insert(t == typeof(GuardRow) ? model : new GuardCleanRow { Guid = Guid.NewGuid() }); return Task.CompletedTask; });
        yield return ("Update(type, model, expr)", (c, t) => { c.Update(t, model, filter); return Task.CompletedTask; });
        yield return ("Update(type, model, conditions)", (c, t) => { c.Update(t, model, conditions()); return Task.CompletedTask; });
        yield return ("Update<T,P>(type, values, expr)", (c, t) => { c.Update(t, setValue, filter); return Task.CompletedTask; });
        yield return ("Update<T,P>(type, expressions, expr)", (c, t) => { c.Update(t, setExpr, filter); return Task.CompletedTask; });
        yield return ("Update<T,P>(type, values, conditions)", (c, t) => { c.Update(t, setValue, conditions()); return Task.CompletedTask; });
        yield return ("Update<T,P>(type, expressions, conditions)", (c, t) => { c.Update(t, setExpr, conditions()); return Task.CompletedTask; });
        yield return ("Delete(type, expr)", (c, t) => { c.Delete(t, filter); return Task.CompletedTask; });
        yield return ("Delete(type, conditions)", (c, t) => { c.Delete(t, conditions()); return Task.CompletedTask; });
        yield return ("DeleteAll(type)", (c, t) => { c.DeleteAll(t); return Task.CompletedTask; });
        yield return ("SelectAggregate(type, query)", (c, t) => { _ = c.SelectAggregate(t, aggregate).ToList(); return Task.CompletedTask; });

        yield return ("SelectAsync(type, expr)", (c, t) => Drain(c.SelectAsync(t, filter)));
        yield return ("SelectAsync(type, conditions)", (c, t) => Drain(c.SelectAsync(t, conditions())));
        yield return ("SelectAsync<T,P>(type, expr, order)", (c, t) => Drain(c.SelectAsync(t, filter, order)));
        yield return ("SelectAsync(types, expr)", (c, t) => Drain(c.SelectAsync(new[] { t }, filter)));
        yield return ("SelectAsync(IEnumerable<Type>, conditions)", (c, t) => Drain(c.SelectAsync((IEnumerable<Type>)new[] { t }, conditions())));
        yield return ("SelectCountAsync(type, expr)", (c, t) => c.SelectCountAsync(t, filter));
        yield return ("SelectCountAsync(type, conditions)", (c, t) => c.SelectCountAsync(t, conditions()));
        yield return ("SelectCountAsync(types, expr)", (c, t) => c.SelectCountAsync(new[] { t }, filter));
        yield return ("InsertAsync(type, model)", (c, t) => c.InsertAsync(t, model));
        yield return ("UpdateAsync(type, model, expr)", (c, t) => c.UpdateAsync(t, model, filter));
        yield return ("UpdateAsync(type, model, conditions)", (c, t) => c.UpdateAsync(t, model, conditions()));
        yield return ("UpdateAsync<T,P>(type, values, expr)", (c, t) => c.UpdateAsync(t, setValue, filter));
        yield return ("UpdateAsync<T,P>(type, expressions, expr)", (c, t) => c.UpdateAsync(t, setExpr, filter));
        yield return ("UpdateAsync<T,P>(type, values, conditions)", (c, t) => c.UpdateAsync(t, setValue, conditions()));
        yield return ("UpdateAsync<T,P>(type, expressions, conditions)", (c, t) => c.UpdateAsync(t, setExpr, conditions()));
        yield return ("DeleteAsync(type, expr)", (c, t) => c.DeleteAsync(t, filter));
        yield return ("DeleteAsync(type, conditions)", (c, t) => c.DeleteAsync(t, conditions()));
        yield return ("DeleteAllAsync(type)", (c, t) => c.DeleteAllAsync(t));
        yield return ("SelectAggregateAsync(type, query)", (c, t) => Drain(c.SelectAggregateAsync(t, aggregate)));
    }

    public static IEnumerable<object[]> EntryPointNames => EntryPoints().Select(e => new object[] { e.Name });

    private static Func<FakeConnector, Type, Task> Call(string name) => EntryPoints().Single(e => e.Name == name).Call;

    [Theory]
    [MemberData(nameof(EntryPointNames))]
    public async Task A_typed_entry_point_refuses_a_rejected_column_by_name_before_it_opens_a_connection(string name)
    {
        var connector = new FakeConnector();

        var act = () => Call(name)(connector, typeof(GuardRow));

        (await act.Should().ThrowAsync<NotSupportedException>(name))
            .WithMessage("Column \"Order\" of entity *GuardRow (property Order, table \"T872GuardRows\") is a reserved word on FakeSQL*");
    }

    [Theory]
    [MemberData(nameof(EntryPointNames))]
    public async Task A_typed_entry_point_lets_a_clean_entity_through(string name)
    {
        var connector = new FakeConnector();
        Exception? thrown = null;

        try { await Call(name)(connector, typeof(GuardCleanRow)); }
        catch (Exception ex) { thrown = ex; }

        thrown.Should().NotBeOfType<NotSupportedException>(name + " must reach the database for a clean entity");
    }

    [Fact]
    public void The_funnel_is_not_virtual()
    {
        typeof(AbstractConnectorBase).GetMethod("LoadUsableTable", BindingFlags.NonPublic | BindingFlags.Instance)!
            .IsVirtual.Should().BeFalse("the check is the funnel; the list is the override point (rule 12)");
    }
}
