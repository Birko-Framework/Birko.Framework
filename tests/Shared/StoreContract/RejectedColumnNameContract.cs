using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>A column named <c>Order</c>: reserved on all four providers.</summary>
[Table("T862OrderRows")]
public class RejectedOrderRow : AbstractModel
{
    public int Order { get; set; }
}

/// <summary>A clean entity created in the same batch as <see cref="RejectedOrderRow"/>.</summary>
[Table("T862CleanRows")]
public class RejectedCleanRow : AbstractModel
{
    public int Amount { get; set; }
}

/// <summary>A column named <c>Rank</c>: reserved on MySQL (since 8.0.2) and nowhere else.</summary>
[Table("T862RankRows")]
public class RejectedRankRow : AbstractModel
{
    public int Rank { get; set; }
}

/// <summary>A column named <c>Value</c>: a keyword everywhere, reserved nowhere, so it must keep working.</summary>
[Table("T862ValueRows")]
public class RejectedValueRow : AbstractModel
{
    public int Value { get; set; }
}

/// <summary>The caller's way out: <c>Order</c> mapped to <c>SortOrder</c> by attribute.</summary>
[Table("T862NamedRows")]
public class RejectedNamedRow : AbstractModel
{
    [NamedField("SortOrder")]
    public int Order { get; set; }
}

/// <summary>The other way out: <c>Group</c> mapped to <c>GroupNo</c> by a fluent <c>HasColumnName</c>.</summary>
public class RejectedFluentRow : AbstractModel
{
    public int Group { get; set; }
}

internal sealed class RejectedFluentRowMapping : IModelMapping<RejectedFluentRow>
{
    public void Configure(ModelMap<RejectedFluentRow> map)
    {
        map.ToTable("T862FluentRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
        map.Property(x => x.Group).HasColumnName("GroupNo");
    }
}

/// <summary>A table that already exists with a column the provider rejects, made through this sibling.</summary>
[Table("T862ExistingRows")]
public class RejectedExistingSibling : AbstractModel
{
    [NamedField("Zq862")]
    public int Order { get; set; }
}

/// <summary>The entity a caller would read that existing table with: <c>Order</c> unmapped.</summary>
[Table("T862ExistingRows")]
public class RejectedExistingRow : AbstractModel
{
    public int Order { get; set; }
}

/// <summary>TASK-872: builds a table with harmless column names, which the test then renames to reserved ones.</summary>
[Table("T872DirectRows")]
public class T872DirectSibling : AbstractModel
{
    [NamedField("Zq872u")] public string? User { get; set; }
    [NamedField("Zq872d")] public string? CURRENT_DATE { get; set; }
    [NamedField("Zq872c")] public string? CURRENT_USER { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-872: the entity a direct connector call reads that renamed table with, its columns unmapped.</summary>
[Table("T872DirectRows")]
public class T872DirectRow : AbstractModel
{
    public string? User { get; set; }
    public string? CURRENT_DATE { get; set; }
    public string? CURRENT_USER { get; set; }
    public int Amount { get; set; }
}

/// <summary>
/// TASK-862 — a column the provider rejects as an identifier (a reserved word, measured live) is refused with
/// <see cref="NotSupportedException"/> naming the column, entity and provider, before any SQL is sent; a column the
/// provider accepts, or one mapped to another name, keeps working.
/// <para>
/// Before: column identifiers are emitted bare (rule 17), so <c>CREATE TABLE</c> failed with the provider's syntax error
/// (MySQL 1064 near 'Rank', PostgreSQL 42601, SQL Server "Incorrect syntax near the keyword", SQLite "near "Order"").
/// Against a table that already exists the store's initialisation re-ran that <c>CREATE TABLE</c> and failed the same
/// way. On SQLite <c>CURRENT_DATE</c> passed the DDL and was read as the current date in a write's WHERE, so a filtered
/// delete matched nothing, silently.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project.</para>
/// </summary>
public abstract class RejectedColumnNameContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh store of <typeparamref name="T"/> whose table does not exist yet.</summary>
    protected abstract IAsyncBulkStore<T> NewStore<T>() where T : AbstractModel;

    /// <summary>The provider's name as the refusal shows it.</summary>
    protected abstract string ProviderName { get; }

    /// <summary>The provider's list, as its connector holds it.</summary>
    protected abstract RejectedColumnNames Refusals { get; }

    /// <summary>Whether this provider rejects <c>Rank</c> (MySQL only).</summary>
    protected abstract bool RejectsRank { get; }

    /// <summary>Names that pass <c>CREATE TABLE</c> but fail or misread in a write's WHERE (SQLite only).</summary>
    protected virtual IReadOnlyCollection<string> RejectedOnlyInWrites => Array.Empty<string>();

    /// <summary>Renames a bare column to a quoted rejected name, the way a table made outside Birko would carry it.</summary>
    protected virtual string RenameToRejectedSql(AbstractConnector connector, string table, string from, string to)
        => $"ALTER TABLE {connector.TableIdentifier(table)} RENAME COLUMN {from} TO {connector.QuoteIdentifier(to)}";

    private static readonly ConcurrentDictionary<Type, bool> FluentRegistered = new();

    private static AbstractConnector ConnectorOf<T>(IAsyncBulkStore<T> store) where T : AbstractModel
        => ((ISqlConnectedStore)store).Connector ?? throw new InvalidOperationException("store has no connector");

    /// <summary>Runs <paramref name="action"/> and returns every command text the connector sent that names <paramref name="table"/>.</summary>
    private static async Task<List<string>> CommandsNaming(AbstractConnector connector, string table, Func<Task> action)
    {
        var seen = new ConcurrentQueue<string>();
        OnExecute handler = text => { if (text != null && text.Contains(table, StringComparison.OrdinalIgnoreCase)) seen.Enqueue(text); };
        connector.OnExecute += handler;
        try { await action(); }
        finally { connector.OnExecute -= handler; }
        return seen.ToList();
    }

    private void ShouldNameTheColumn(NotSupportedException ex, string column, Type entity)
    {
        ex.Message.Should().Contain($"\"{column}\"").And.Contain(entity.FullName!).And.Contain(ProviderName)
            .And.Contain("reserved word").And.Contain("[NamedField(").And.Contain("HasColumnName(");
    }

    [Fact]
    public async Task A_rejected_column_is_refused_by_name_before_any_SQL_is_sent()
    {
        if (!Ready()) return;
        var store = NewStore<RejectedOrderRow>();
        NotSupportedException? refusal = null;

        var sent = await CommandsNaming(ConnectorOf(store), "T862OrderRows", async () =>
        {
            refusal = (await FluentActions.Awaiting(() => store.CreateAsync(new RejectedOrderRow { Order = 1 }))
                .Should().ThrowAsync<NotSupportedException>()).Which;
        });

        ShouldNameTheColumn(refusal!, "Order", typeof(RejectedOrderRow));
        sent.Should().BeEmpty("the refusal comes before CREATE TABLE, so nothing naming the table reaches the server");
    }

    [Fact]
    public async Task Every_operation_refuses_not_only_the_first()
    {
        if (!Ready()) return;
        var store = NewStore<RejectedOrderRow>();

        await FluentActions.Awaiting(() => store.InitAsync()).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => store.ReadAsync(x => x.Order == 1)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => store.CountAsync(x => x.Order == 1)).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => store.UpdateAsync(x => x.Order == 1, new PropertyUpdate<RejectedOrderRow>().Set(x => x.Order, 2)))
            .Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => store.DeleteAsync(x => x.Order == 1)).Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void A_batch_with_one_rejected_table_creates_none_of_them()
    {
        if (!Ready()) return;
        var connector = ConnectorOf(NewStore<RejectedCleanRow>());
        var sent = new ConcurrentQueue<string>();
        OnExecute handler = text => { if (text != null && text.Contains("T862CleanRows", StringComparison.OrdinalIgnoreCase)) sent.Enqueue(text); };
        connector.OnExecute += handler;
        try
        {
            FluentActions.Invoking(() => connector.CreateTable(new[] { typeof(RejectedCleanRow), typeof(RejectedOrderRow) }))
                .Should().Throw<NotSupportedException>().WithMessage("*\"Order\"*");
        }
        finally { connector.OnExecute -= handler; }

        sent.Should().BeEmpty("every table is checked before the first one is created");
    }

    [Fact]
    public async Task A_read_against_a_table_that_already_exists_refuses_by_name()
    {
        if (!Ready()) return;
        var sibling = NewStore<RejectedExistingSibling>();
        await sibling.DestroyAsync();
        await sibling.CreateAsync(new RejectedExistingSibling { Order = 1 });
        var connector = ConnectorOf(sibling);
        connector.DoCommand(
            command => command.CommandText = RenameToRejectedSql(connector, "T862ExistingRows", "Zq862", "Order"),
            command => command.ExecuteNonQuery());

        try
        {
            var store = NewStore<RejectedExistingRow>();
            NotSupportedException? refusal = null;
            var sent = await CommandsNaming(ConnectorOf(store), "T862ExistingRows", async () =>
            {
                refusal = (await FluentActions.Awaiting(() => store.ReadAsync(x => x.Order == 1))
                    .Should().ThrowAsync<NotSupportedException>()).Which;
            });

            ShouldNameTheColumn(refusal!, "Order", typeof(RejectedExistingRow));
            sent.Should().BeEmpty("the store's initialisation runs the check before its CREATE TABLE IF NOT EXISTS");
        }
        finally
        {
            await sibling.DestroyAsync();
        }
    }

    [Fact]
    public void Adding_a_rejected_column_to_an_existing_table_is_refused_before_the_ALTER()
    {
        if (!Ready()) return;
        var connector = ConnectorOf(NewStore<RejectedValueRow>());
        var table = DataBase.LoadTable(typeof(RejectedOrderRow));
        var sent = new ConcurrentQueue<string>();
        OnExecute handler = text => { if (text != null && text.Contains("ALTER", StringComparison.OrdinalIgnoreCase) && text.Contains("T862ValueRows")) sent.Enqueue(text); };
        connector.OnExecute += handler;
        try
        {
            FluentActions.Invoking(() => connector.AlterTableAdd("T862ValueRows", table.Fields.Values.Where(f => f.Name == "Order")))
                .Should().Throw<NotSupportedException>().WithMessage("*\"Order\"*reserved word*");
        }
        finally { connector.OnExecute -= handler; }

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Rank_is_refused_only_where_the_provider_reserves_it()
    {
        if (!Ready()) return;
        var store = NewStore<RejectedRankRow>();
        if (RejectsRank)
        {
            var refusal = (await FluentActions.Awaiting(() => store.CreateAsync(new RejectedRankRow { Rank = 1 }))
                .Should().ThrowAsync<NotSupportedException>()).Which;
            ShouldNameTheColumn(refusal, "Rank", typeof(RejectedRankRow));
            return;
        }

        await store.DestroyAsync();
        await store.CreateAsync(new RejectedRankRow { Rank = 7 });
        (await store.ReadAsync(x => x.Rank == 7)).Should().ContainSingle();
        await store.DestroyAsync();
    }

    [Fact]
    public async Task A_keyword_the_provider_does_not_reserve_keeps_working()
    {
        if (!Ready()) return;
        var store = NewStore<RejectedValueRow>();
        await store.DestroyAsync();

        await store.CreateAsync(new RejectedValueRow { Value = 3 });
        await store.UpdateAsync(x => x.Value == 3, new PropertyUpdate<RejectedValueRow>().Set(x => x.Value, 4));
        (await store.ReadAsync(x => x.Value == 4)).Should().ContainSingle();
        await store.DeleteAsync(x => x.Value == 4);
        (await store.CountAsync()).Should().Be(0);
        await store.DestroyAsync();
    }

    [Fact]
    public async Task A_rejected_property_mapped_by_NamedField_round_trips()
    {
        if (!Ready()) return;
        var store = NewStore<RejectedNamedRow>();
        await store.DestroyAsync();

        await store.CreateAsync(new RejectedNamedRow { Order = 1 });
        await store.CreateAsync(new RejectedNamedRow { Order = 2 });
        await store.UpdateAsync(x => x.Order == 1, new PropertyUpdate<RejectedNamedRow>().Set(x => x.Order, 5));
        (await store.ReadAsync(x => x.Order == 5)).Should().ContainSingle();
        await store.DeleteAsync(x => x.Order == 2);
        (await store.ReadAsync(x => x.Order > 0)).Select(x => x.Order).Should().Equal(5);
        await store.DestroyAsync();
    }

    [Fact]
    public async Task A_rejected_property_mapped_by_HasColumnName_round_trips()
    {
        if (!Ready()) return;
        if (FluentRegistered.TryAdd(typeof(RejectedFluentRow), true))
        {
            var registry = new ModelMapRegistry();
            registry.Register(new RejectedFluentRowMapping());
            registry.ApplyToDatabase();
        }

        var store = NewStore<RejectedFluentRow>();
        await store.DestroyAsync();
        await store.CreateAsync(new RejectedFluentRow { Group = 1 });
        await store.UpdateAsync(x => x.Group == 1, new PropertyUpdate<RejectedFluentRow>().Set(x => x.Group, 6));
        (await store.ReadAsync(x => x.Group == 6)).Should().ContainSingle();
        await store.DeleteAsync(x => x.Group == 6);
        (await store.CountAsync()).Should().Be(0);
        await store.DestroyAsync();
    }

    /// <summary>
    /// Re-measures the list against the live server: every name on it fails <c>CREATE TABLE</c> (or, for the few that
    /// pass it, a write's WHERE), so a name that a server version starts to accept shows up here, not in a consumer.
    /// </summary>
    [Fact]
    public void Every_refused_name_is_rejected_by_the_server()
    {
        if (!Ready()) return;
        var connector = ConnectorOf(NewStore<RejectedValueRow>());
        var table = connector.TableIdentifier("T862Probe");
        void Run(string sql) => connector.DoCommand(c => c.CommandText = sql, c => c.ExecuteNonQuery());
        void Drop() { try { Run("DROP TABLE IF EXISTS " + table); } catch { } }

        var accepted = new List<string>();
        foreach (var name in Refusals.Names.Except(RejectedOnlyInWrites, StringComparer.OrdinalIgnoreCase))
        {
            Drop();
            try { Run($"CREATE TABLE {table} ({name} INT)"); accepted.Add(name); }
            catch (Exception) { }
        }
        Drop();
        accepted.Should().BeEmpty($"every name on {ProviderName}'s list must fail CREATE TABLE; these no longer do");

        foreach (var name in RejectedOnlyInWrites)
        {
            Drop();
            Run($"CREATE TABLE {table} ({connector.QuoteIdentifier(name)} INT)");
            Run($"INSERT INTO {table} ({connector.QuoteIdentifier(name)}) VALUES (1)");
            var deleted = -1;
            try { connector.DoCommand(c => c.CommandText = $"DELETE FROM {table} WHERE {name} = 1", c => deleted = c.ExecuteNonQuery()); }
            catch (Exception) { deleted = -2; }
            deleted.Should().NotBe(1, $"{name} must fail or misread in a write's WHERE to be on the list");
        }
        Drop();

        foreach (var name in new[] { "Value", "Name", "Amount" })
        {
            Drop();
            Run($"CREATE TABLE {table} ({name} INT)");
            Refusals.Contains(name).Should().BeFalse();
        }
        Drop();
    }

    /// <summary>
    /// The provider's value-function name used as a column (TASK-872): <c>User</c> on PostgreSQL and SQL Server,
    /// <c>CURRENT_USER</c> on MySQL, <c>CURRENT_DATE</c> on SQLite. Bare in a WHERE, each reads as the function.
    /// </summary>
    protected abstract string ValueFunctionColumn { get; }

    private void RebuildDirectTable()
    {
        var sibling = NewStore<T872DirectSibling>();
        sibling.DestroyAsync().GetAwaiter().GetResult();
        sibling.CreateAsync(new T872DirectSibling { User = "a", CURRENT_DATE = "a", CURRENT_USER = "a", Amount = 1 }).GetAwaiter().GetResult();
        sibling.CreateAsync(new T872DirectSibling { User = "b", CURRENT_DATE = "b", CURRENT_USER = "b", Amount = 2 }).GetAwaiter().GetResult();
        var connector = ConnectorOf(sibling);
        foreach (var (from, to) in new[] { ("Zq872u", "User"), ("Zq872d", "CURRENT_DATE"), ("Zq872c", "CURRENT_USER") })
            connector.DoCommand(c => c.CommandText = RenameToRejectedSql(connector, "T872DirectRows", from, to), c => c.ExecuteNonQuery());
    }

    /// <summary>
    /// TASK-872 — a direct connector call (<c>store.Connector</c>, a repository's <c>Connector</c>) on a table Birko did
    /// not build refuses an entity whose column the provider rejects, by name, before any SQL is sent, and writes nothing.
    /// <para>
    /// Before, measured live: the filter compared the value function with itself, which C# matches on no row.
    /// <c>Delete</c> removed 2 of 2 rows and the expression-keyed <c>Update</c> rewrote 2 of 2, silently, on all four
    /// providers. Reads named the column qualified, so they returned 0 rows on SQLite and MySQL (right) and failed on
    /// PostgreSQL and SQL Server; <c>Insert</c> and the model <c>Update</c> failed on the server (SQLite's insert ran).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_direct_connector_call_on_a_table_Birko_did_not_build_refuses_by_name_and_writes_nothing()
    {
        if (!Ready()) return;
        RebuildDirectTable();
        var connector = ConnectorOf(NewStore<RejectedValueRow>());
        var async = (AbstractAsyncConnector)connector;
        object? value = null;
        connector.DoCommand(c => c.CommandText = "SELECT " + ValueFunctionColumn, c => value = c.ExecuteScalar());
        var x = System.Linq.Expressions.Expression.Parameter(typeof(T872DirectRow), "x");
        var filter = System.Linq.Expressions.Expression.Lambda<Func<T872DirectRow, bool>>(
            System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(x, ValueFunctionColumn),
                System.Linq.Expressions.Expression.Constant(Convert.ToString(value), typeof(string))), x);
        var type = typeof(T872DirectRow);
        var model = new T872DirectRow { Guid = Guid.NewGuid(), User = "z", CURRENT_DATE = "z", CURRENT_USER = "z", Amount = 5 };
        var set = new Dictionary<System.Linq.Expressions.Expression<Func<T872DirectRow, int>>, object> { { r => r.Amount, 9 } };

        var calls = new (string Name, Func<Task> Call)[]
        {
            ("Select", () => { _ = connector.Select(type, filter).ToList(); return Task.CompletedTask; }),
            ("SelectCount", () => { connector.SelectCount(type, filter); return Task.CompletedTask; }),
            ("Insert", () => { connector.Insert(type, model); return Task.CompletedTask; }),
            ("Update(model)", () => { connector.Update(type, model, filter); return Task.CompletedTask; }),
            ("Update(values)", () => { connector.Update(type, set, filter); return Task.CompletedTask; }),
            ("Delete", () => { connector.Delete(type, filter); return Task.CompletedTask; }),
            ("SelectAsync", async () => { await foreach (var _ in async.SelectAsync(type, filter)) { } }),
            ("SelectCountAsync", () => async.SelectCountAsync(type, filter)),
            ("InsertAsync", () => async.InsertAsync(type, model)),
            ("UpdateAsync(values)", () => async.UpdateAsync(type, set, filter)),
            ("DeleteAsync", () => async.DeleteAsync(type, filter)),
        };

        try
        {
            foreach (var (name, call) in calls)
            {
                NotSupportedException? refusal = null;
                var sent = await CommandsNaming(connector, "T872DirectRows", async () =>
                {
                    refusal = (await FluentActions.Awaiting(call).Should().ThrowAsync<NotSupportedException>(name)).Which;
                });

                refusal!.Message.Should().Contain($"\"{ValueFunctionColumn}\"").And.Contain(type.FullName!).And.Contain(ProviderName);
                sent.Should().BeEmpty($"{name} is refused before any SQL naming the table is sent");
            }
        }
        finally
        {
            long rows = -1, untouched = -1;
            var table = connector.TableIdentifier("T872DirectRows");
            connector.DoCommand(c => c.CommandText = "SELECT COUNT(*) FROM " + table, c => rows = Convert.ToInt64(c.ExecuteScalar()));
            connector.DoCommand(c => c.CommandText = "SELECT COUNT(*) FROM " + table + " WHERE Amount IN (1, 2)", c => untouched = Convert.ToInt64(c.ExecuteScalar()));
            NewStore<T872DirectSibling>().DestroyAsync().GetAwaiter().GetResult();
            rows.Should().Be(2, "nothing was deleted or inserted");
            untouched.Should().Be(2, "nothing was updated");
        }
    }

    /// <summary>
    /// TASK-891 — the connector overloads that take a resolved <see cref="Birko.Data.SQL.Tables.Table"/> or a table name
    /// (a store's bulk update runs through the name one) refuse a column the provider rejects, by name, before any SQL is
    /// sent, and write nothing: a <c>Table</c> through its columns, as the typed overloads do (TASK-872); a table name through
    /// the columns the statement names, the ones it writes and the bare ones its filter compares.
    /// <para>
    /// Before, measured live: the filter <c>User = 'postgres'</c> (<c>CURRENT_USER</c> on MySQL, <c>CURRENT_DATE</c> on
    /// SQLite) compared the value function with itself, so the delete and every update hit 2 of 2 rows that C# matches none
    /// of, and the counts answered 2; the failure message lists each call's outcome.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_Table_or_table_name_connector_call_refuses_a_rejected_column_and_writes_nothing()
    {
        if (!Ready()) return;
        var connector = ConnectorOf(NewStore<RejectedValueRow>());
        var async = (AbstractAsyncConnector)connector;
        object? value = null;
        connector.DoCommand(c => c.CommandText = "SELECT " + ValueFunctionColumn, c => value = c.ExecuteScalar());
        var x = System.Linq.Expressions.Expression.Parameter(typeof(T872DirectRow), "x");
        var filter = System.Linq.Expressions.Expression.Lambda<Func<T872DirectRow, bool>>(
            System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(x, ValueFunctionColumn),
                System.Linq.Expressions.Expression.Constant(Convert.ToString(value), typeof(string))), x);
        var conditions = DataBase.ParseConditionExpression(filter).ToList();
        var table = DataBase.LoadTable(typeof(T872DirectRow));
        const string name = "T872DirectRows";
        var model = new T872DirectRow { Guid = Guid.NewGuid(), User = "z", CURRENT_DATE = "z", CURRENT_USER = "z", Amount = 5 };
        var set = new Dictionary<System.Linq.Expressions.Expression<Func<T872DirectRow, int>>, object> { { r => r.Amount, 9 } };
        var setExpr = new Dictionary<System.Linq.Expressions.Expression<Func<T872DirectRow, int>>, System.Linq.Expressions.Expression<Func<T872DirectRow, int>>> { { r => r.Amount, r => r.Amount + 7 } };
        var values = new Dictionary<string, object> { { "Amount", 9 } };
        var fields = new Dictionary<int, string> { { 0, "Amount" } };
        var row = new Dictionary<string, object> { { "Guid", Guid.NewGuid() }, { ValueFunctionColumn, "z" }, { "Amount", 5 } };
        long counted = -1;

        var calls = new (string Name, Func<Task> Call)[]
        {
            ("Delete(Table)", () => { connector.Delete(table, conditions); return Task.CompletedTask; }),
            ("Update(Table, model)", () => { connector.Update(table, model, conditions); return Task.CompletedTask; }),
            ("Update<T,P>(Table, values)", () => { connector.Update(table, set, conditions); return Task.CompletedTask; }),
            ("Update<T,P>(Table, expressions)", () => { connector.Update(table, setExpr, conditions); return Task.CompletedTask; }),
            ("Update(Table, IDictionary)", () => { connector.Update(table, values, conditions); return Task.CompletedTask; }),
            ("Insert(Table, model)", () => { connector.Insert(table, (object)model); return Task.CompletedTask; }),
            ("SelectCount(Tables)", () => { counted = connector.SelectCount(new[] { table }, conditions); return Task.CompletedTask; }),
            ("Select(Tables)", () => { counted = connector.Select(new[] { table }, (f, r) => new object[] { 1 }, conditions).LongCount(); return Task.CompletedTask; }),
            ("Update<T,P>(name, values)", () => { connector.Update(name, set, conditions); return Task.CompletedTask; }),
            ("Update<T,P>(name, expressions)", () => { connector.Update(name, setExpr, conditions); return Task.CompletedTask; }),
            ("Update(name, fields, values)", () => { connector.Update(name, fields, values, conditions); return Task.CompletedTask; }),
            ("SelectCount(names)", () => { counted = connector.SelectCount(new[] { name }, conditions); return Task.CompletedTask; }),
            ("Insert(name, rows)", () => { connector.Insert(name, new[] { row }); return Task.CompletedTask; }),
            ("DeleteAsync(Table)", () => async.DeleteAsync(table, conditions)),
            ("UpdateAsync(Table, model)", () => async.UpdateAsync(table, model, conditions)),
            ("UpdateAsync<T,P>(Table, values)", () => async.UpdateAsync(table, set, conditions)),
            ("UpdateAsync(Table, IDictionary)", () => async.UpdateAsync(table, values, conditions)),
            ("InsertAsync(Table, model)", () => async.InsertAsync(table, (object)model)),
            ("SelectCountAsync(Tables)", async () => counted = await async.SelectCountAsync(new[] { table }, conditions)),
            ("UpdateAsync<T,P>(name, values)", () => async.UpdateAsync(name, set, conditions)),
            ("UpdateAsync(name, fields, values)", () => async.UpdateAsync(name, fields, values, conditions)),
            ("SelectCountAsync(names)", async () => counted = await async.SelectCountAsync(new[] { name }, conditions)),
            ("InsertAsync(name, rows)", () => async.InsertAsync(name, new[] { row })),
        };

        var failures = new List<string>();
        try
        {
            foreach (var (call, run) in calls)
            {
                RebuildDirectTable();
                counted = -1;
                Exception? error = null;
                var sent = await CommandsNaming(connector, name, async () =>
                {
                    try { await run(); }
                    catch (Exception ex) { error = ex; }
                });
                long rows = -1, untouched = -1;
                var identifier = connector.TableIdentifier(name);
                connector.DoCommand(c => c.CommandText = "SELECT COUNT(*) FROM " + identifier, c => rows = Convert.ToInt64(c.ExecuteScalar()));
                connector.DoCommand(c => c.CommandText = "SELECT COUNT(*) FROM " + identifier + " WHERE Amount IN (1, 2)", c => untouched = Convert.ToInt64(c.ExecuteScalar()));
                var outcome = error == null
                    ? $"ran; {rows} rows, {untouched} untouched" + (counted >= 0 ? $", answered {counted}" : string.Empty)
                    : $"{error.GetType().Name}: {error.Message.Split('\n')[0]}; {rows} rows, {untouched} untouched";
                var refused = error is NotSupportedException
                    && error.Message.Contains($"\"{ValueFunctionColumn}\"") && error.Message.Contains(ProviderName);
                if (!refused || sent.Count > 0 || rows != 2 || untouched != 2)
                    failures.Add($"{call}: {outcome}" + (sent.Count > 0 ? $"; sent {sent.Count} statement(s)" : string.Empty));
            }
        }
        finally
        {
            await NewStore<T872DirectSibling>().DestroyAsync();
        }

        string.Join(" | ", failures).Should().BeEmpty($"every Table and table-name overload refuses {ValueFunctionColumn} by name before any SQL on {ProviderName}");
    }

    /// <summary>
    /// TASK-901 — the providers' type-keyed bulk writes (<c>BulkInsert</c> / <c>BulkUpdate</c> / <c>BulkDelete</c>, sync and
    /// async), reached directly on the connector, refuse an entity whose column the provider rejects, by name, before any SQL
    /// is sent, and write nothing: they resolve the entity through <c>LoadUsableTable</c>, as every typed overload does
    /// (TASK-872). Two tables: <c>T872DirectRows</c>, whose rejected columns are written (SET, INSERT column list) and whose
    /// key is <c>Guid</c>, and <c>T901KeyRows</c>, whose rejected column is the primary key, so the bulk WHERE names it bare
    /// and compares the provider's value function with itself.
    /// <para>The failure message lists each call's outcome.</para>
    /// </summary>
    [Fact]
    public async Task A_bulk_write_by_type_refuses_a_rejected_column_and_writes_nothing()
    {
        if (!Ready()) return;
        var connector = ConnectorOf(NewStore<RejectedValueRow>());
        object? value = null;
        connector.DoCommand(c => c.CommandText = "SELECT " + ValueFunctionColumn, c => value = c.ExecuteScalar());
        var functionValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

        // The key entity whose primary key column is this provider's value function; its key is the function's value, so
        // the bare WHERE matches every row, where C# matches neither stored key ("a", "b").
        var keyType = ValueFunctionColumn switch
        {
            "User" => typeof(T901UserKeyRow),
            "CURRENT_USER" => typeof(T901CurrentUserKeyRow),
            _ => typeof(T901CurrentDateKeyRow),
        };
        var keyRow = Activator.CreateInstance(keyType)!;
        keyType.GetProperty("Key")!.SetValue(keyRow, functionValue);
        keyType.GetProperty("Amount")!.SetValue(keyRow, 9);
        ((AbstractModel)keyRow).Guid = Guid.NewGuid();

        var direct = new object[] { new T872DirectRow { Guid = Guid.NewGuid(), User = "z", CURRENT_DATE = "z", CURRENT_USER = "z", Amount = 9 } };
        var keyed = new[] { keyRow };

        var plan = new List<(string Name, string Table, Action Rebuild, Func<Task> Call)>();
        foreach (var (table, type, models, rebuild) in new (string, Type, object[], Action)[]
        {
            ("T872DirectRows", typeof(T872DirectRow), direct, RebuildDirectTable),
            ("T901KeyRows", keyType, keyed, RebuildKeyTable),
        })
        {
            foreach (var method in new[] { "BulkInsert", "BulkUpdate", "BulkDelete" })
            {
                plan.Add(($"{method}({table})", table, rebuild, () => InvokeBulk(connector, method, type, models, async: false)));
                plan.Add(($"{method}Async({table})", table, rebuild, () => InvokeBulk(connector, method + "Async", type, models, async: true)));
            }
        }

        var failures = new List<string>();
        try
        {
            foreach (var (call, table, rebuild, run) in plan)
            {
                rebuild();
                Exception? error = null;
                var sent = await CommandsNaming(connector, table, async () =>
                {
                    try { await run(); }
                    catch (Exception ex) { error = ex; }
                });
                long rows = -1, untouched = -1;
                var identifier = connector.TableIdentifier(table);
                connector.DoCommand(c => c.CommandText = "SELECT COUNT(*) FROM " + identifier, c => rows = Convert.ToInt64(c.ExecuteScalar()));
                connector.DoCommand(c => c.CommandText = "SELECT COUNT(*) FROM " + identifier + " WHERE Amount IN (1, 2)", c => untouched = Convert.ToInt64(c.ExecuteScalar()));
                var outcome = error == null
                    ? $"ran; {rows} rows, {untouched} untouched"
                    : $"{error.GetType().Name}: {error.Message.Split('\n')[0]}; {rows} rows, {untouched} untouched";
                var refused = error is NotSupportedException
                    && error.Message.Contains($"\"{ValueFunctionColumn}\"") && error.Message.Contains(ProviderName);
                if (!refused || sent.Count > 0 || rows != 2 || untouched != 2)
                    failures.Add($"{call}: {outcome}" + (sent.Count > 0 ? $"; sent {sent.Count} statement(s)" : string.Empty));
            }
        }
        finally
        {
            await NewStore<T872DirectSibling>().DestroyAsync();
            await NewStore<T901KeySibling>().DestroyAsync();
        }

        string.Join(" | ", failures).Should().BeEmpty($"every type-keyed bulk write refuses {ValueFunctionColumn} by name before any SQL on {ProviderName}");
    }

    /// <summary>Builds <c>T901KeyRows</c> with keys "a" and "b" under a harmless name, then renames the key column.</summary>
    private void RebuildKeyTable()
    {
        var sibling = NewStore<T901KeySibling>();
        sibling.DestroyAsync().GetAwaiter().GetResult();
        sibling.CreateAsync(new T901KeySibling { Key = "a", Amount = 1 }).GetAwaiter().GetResult();
        sibling.CreateAsync(new T901KeySibling { Key = "b", Amount = 2 }).GetAwaiter().GetResult();
        var connector = ConnectorOf(sibling);
        connector.DoCommand(c => c.CommandText = RenameToRejectedSql(connector, "T901KeyRows", "Zq901k", ValueFunctionColumn), c => c.ExecuteNonQuery());
    }

    /// <summary>Calls the provider connector's own bulk method, which is not on <see cref="AbstractConnector"/>.</summary>
    private static async Task InvokeBulk(AbstractConnector connector, string method, Type type, object[] models, bool async)
    {
        var parameters = async
            ? new[] { typeof(Type), typeof(IEnumerable<object>), typeof(System.Threading.CancellationToken) }
            : new[] { typeof(Type), typeof(IEnumerable<object>) };
        var args = async
            ? new object[] { type, models, System.Threading.CancellationToken.None }
            : new object[] { type, models };
        var info = connector.GetType().GetMethod(method, parameters)
                   ?? throw new InvalidOperationException($"{connector.GetType().Name} has no {method}");
        try
        {
            var result = info.Invoke(connector, args);
            if (result is Task task) await task;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}

/// <summary>TASK-901: builds <c>T901KeyRows</c> with a harmless key column name, which the test renames.</summary>
[Table("T901KeyRows")]
public class T901KeySibling : AbstractModel
{
    [PrimaryField, MaxLengthField(64), NamedField("Zq901k")] public string? Key { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-901: the key entity a direct bulk call reads that renamed table with (PostgreSQL, SQL Server).</summary>
[Table("T901KeyRows")]
public class T901UserKeyRow : AbstractModel
{
    [PrimaryField, MaxLengthField(64), NamedField("User")] public string? Key { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-901: as <see cref="T901UserKeyRow"/>, for MySQL.</summary>
[Table("T901KeyRows")]
public class T901CurrentUserKeyRow : AbstractModel
{
    [PrimaryField, MaxLengthField(64), NamedField("CURRENT_USER")] public string? Key { get; set; }
    public int Amount { get; set; }
}

/// <summary>TASK-901: as <see cref="T901UserKeyRow"/>, for SQLite.</summary>
[Table("T901KeyRows")]
public class T901CurrentDateKeyRow : AbstractModel
{
    [PrimaryField, MaxLengthField(64), NamedField("CURRENT_DATE")] public string? Key { get; set; }
    public int Amount { get; set; }
}
