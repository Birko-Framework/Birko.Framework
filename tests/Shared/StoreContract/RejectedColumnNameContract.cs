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
}
