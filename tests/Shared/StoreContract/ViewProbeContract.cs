using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Tests.Shared.StoreContract;

[Table("T901PSources")]
public class T901PSource : AbstractModel
{
    public string? Login { get; set; }
    public int Amount { get; set; }
}

[Table("T901PTags")]
public class T901PTag : AbstractModel
{
    public Guid SourceId { get; set; }
    public string? Tag { get; set; }
}

/// <summary>An Auto-mode view: read persistent when the view exists, on the fly when it does not.</summary>
[View(typeof(T901PSource), typeof(T901PTag), nameof(T901PSource.Guid), nameof(T901PTag.SourceId), name: "T901PView", QueryMode = ViewQueryMode.Auto)]
public class T901PView
{
    [ViewField(typeof(T901PSource), nameof(T901PSource.Login))]
    public string? Login { get; set; }

    [ViewField(typeof(T901PTag), nameof(T901PTag.Tag))]
    public string? Tag { get; set; }
}

/// <summary>
/// TASK-901 — the base view-existence probe (<c>AbstractConnector.ViewExists</c> / <c>ViewExistsAsync</c>), which runs
/// when the provider's View package (<c>Birko.Data.SQL.{Provider}.View</c>) is not loaded, and Auto mode's cache of its
/// answer.
/// <para>
/// Linked into the provider test projects, which load <c>Birko.Data.SQL.View</c> but not the provider's View package, the
/// composition every consumer in the tree uses. Before, measured: the probe of a missing view threw on all four providers,
/// because the missing relation reaches the probe rewrapped by <c>EnsureSchemaAndReport</c> as a bare
/// <see cref="Exception"/> its <c>catch (DbException)</c> does not match (rule 9). So an Auto read of a view that was never
/// created failed instead of reading on the fly, and <c>CreateViewIfNotExists</c> and <c>SqlViewManager.EnsureAsync</c> of
/// a new view failed. A failure to reach the database, which is a <see cref="System.Data.Common.DbException"/> raised before
/// that funnel, answered "no such view" (rule 26). And an Auto view dropped outside the connector stayed cached as
/// existing, so every later read failed until <c>InvalidateViewExistsCache</c>.
/// </para>
/// </summary>
public abstract class ViewProbeContract : IDisposable
{
    protected readonly ITestOutputHelper Output;
    private AbstractConnector? _connector;

    protected ViewProbeContract(ITestOutputHelper output) => Output = output;

    protected virtual bool Ready() => true;

    /// <summary>A new connector (not the process-wide cached one, so its view cache starts empty) on the live database.</summary>
    protected abstract AbstractConnector NewConnector();

    /// <summary>A connector whose database cannot be reached (wrong password, or a path that cannot be opened).</summary>
    protected abstract AbstractConnector UnreachableConnector();

    protected abstract string ProviderName { get; }

    private const string ViewName = "T901PView";

    /// <summary>
    /// A statement the view probe sent. TASK-917: the probe is the dialect's catalogue query, which asks for the view's
    /// name as a VALUE — <c>OnExecute</c> reports it with parameters inlined, so the name appears single-quoted, while a
    /// read names the view as a quoted identifier (<c>"…"</c>, <c>[…]</c>, <c>`…`</c>). The probe used to be a
    /// <c>SELECT … WHERE 1=0</c> from the name, and these checks matched <c>1=0</c>; kept, that would let a "nothing
    /// probes" assertion pass whatever the connector did.
    /// </summary>
    private static bool IsViewProbe(string statement) => statement.Contains("'" + ViewName + "'", StringComparison.Ordinal);

    private AbstractConnector Connector()
    {
        if (_connector != null) return _connector;
        var connector = NewConnector();
        Drop(connector);
        connector.CreateTable(new[] { typeof(T901PSource), typeof(T901PTag) });
        var a = new T901PSource { Guid = Guid.NewGuid(), Login = "a", Amount = 1 };
        var b = new T901PSource { Guid = Guid.NewGuid(), Login = "b", Amount = 2 };
        connector.Insert(typeof(T901PSource), a);
        connector.Insert(typeof(T901PSource), b);
        connector.Insert(typeof(T901PTag), new T901PTag { Guid = Guid.NewGuid(), SourceId = a.Guid!.Value, Tag = "x" });
        connector.Insert(typeof(T901PTag), new T901PTag { Guid = Guid.NewGuid(), SourceId = b.Guid!.Value, Tag = "y" });
        _connector = connector;
        return connector;
    }

    private static void Drop(AbstractConnector connector)
    {
        try { DropViewOutside(connector); } catch { }
        try { connector.DropTable(new[] { typeof(T901PSource), typeof(T901PTag) }); } catch { }
        connector.ClearViewExistsCache();
    }

    /// <summary>Drops the view with raw DDL, so the connector's existence cache is not told.</summary>
    private static void DropViewOutside(AbstractConnector connector)
        => connector.DoCommand(c => c.CommandText = "DROP VIEW IF EXISTS " + connector.QuoteIdentifier(ViewName), c => c.ExecuteNonQuery());

    /// <summary>
    /// Creates the view with plain <c>CREATE VIEW</c>, so the connector's existence cache is not told. Not
    /// <c>connector.CreateView</c>: without the provider's View package it renders <c>CREATE OR REPLACE VIEW</c>, which
    /// SQLite and SQL Server reject (TASK-901, filed separately).
    /// </summary>
    private static void CreateViewOutside(AbstractConnector connector)
    {
        DropViewOutside(connector);
        var sql = "CREATE VIEW " + connector.QuoteIdentifier(ViewName) + " AS SELECT s.Login AS Login, t.Tag AS Tag FROM "
                  + connector.TableIdentifier("T901PSources") + " s INNER JOIN " + connector.TableIdentifier("T901PTags")
                  + " t ON s.Guid = t.SourceId";
        connector.DoCommand(c => c.CommandText = sql, c => c.ExecuteNonQuery());
    }

    /// <summary>Releases what the binding holds (a SQLite file), after the fixture is dropped.</summary>
    protected virtual void DisposeProvider() { }

    public void Dispose()
    {
        if (_connector != null) Drop(_connector);
        DisposeProvider();
    }

    private static List<string> Logins(AbstractConnector connector)
        => connector.SelectView(typeof(T901PView)).Cast<T901PView>().Select(v => v.Login!).OrderBy(x => x).ToList();

    private static async Task<List<string>> LoginsAsync(AbstractAsyncConnector connector)
    {
        var rows = new List<string>();
        await foreach (var row in connector.SelectAsync(DataBase.LoadView(typeof(T901PView)), (fields, reader) =>
        {
            var data = new T901PView();
            DataBase.ReadView(reader, data);
            return data;
        }))
        {
            rows.Add(((T901PView)row).Login!);
        }
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>Runs <paramref name="action"/> and returns every statement the connector sent.</summary>
    private static async Task<List<string>> Statements(AbstractConnector connector, Func<Task> action)
    {
        var seen = new ConcurrentQueue<string>();
        OnExecute handler = text => { if (text != null) seen.Enqueue(text); };
        connector.OnExecute += handler;
        try { await action(); }
        finally { connector.OnExecute -= handler; }
        return seen.ToList();
    }

    /// <summary>Whether <paramref name="statements"/> read the persistent view (by name) rather than its source tables.</summary>
    private static bool ReadThePersistentView(IEnumerable<string> statements)
        => statements.Any(s => s.Contains(ViewName, StringComparison.OrdinalIgnoreCase)
                               && !s.Contains("T901PSources", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> Outcome(Func<Task<object?>> call)
    {
        try { return Convert.ToString(await call()) ?? "null"; }
        catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}"; }
    }

    [Fact]
    public async Task The_probe_answers_false_for_a_missing_view_and_true_for_an_existing_one()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;

        var sync = await Outcome(() => Task.FromResult<object?>(connector.ViewExists(ViewName)));
        var asynchronous = await Outcome(async () => await async.ViewExistsAsync(ViewName));
        Output.WriteLine($"MEASURE {ProviderName} | missing view | ViewExists | {sync}");
        Output.WriteLine($"MEASURE {ProviderName} | missing view | ViewExistsAsync | {asynchronous}");
        sync.Should().Be("False");
        asynchronous.Should().Be("False");

        CreateViewOutside(connector);
        connector.ViewExists(ViewName).Should().BeTrue();
        (await async.ViewExistsAsync(ViewName)).Should().BeTrue();
    }

    /// <summary>
    /// A probe that answers "no such view" has not failed, so it is not reported as a failure: no <c>OnException</c>
    /// subscriber is told, no schema escape is recorded and <c>OnInit</c> is not raised. A missing table read by a store is
    /// answered the same way (the reader's missing-table swallow runs before the failure funnel).
    /// </summary>
    [Fact]
    public async Task A_probe_answering_no_reports_no_failure()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;
        var reported = new ConcurrentQueue<string>();
        var inits = 0;
        OnException onException = (ex, text) => reported.Enqueue(ex.GetType().Name);
        InitConnector onInit = _ => System.Threading.Interlocked.Increment(ref inits);
        var escapesBefore = connector.SchemaEscapes.Count;
        connector.OnException += onException;
        connector.OnInit += onInit;
        try
        {
            try { connector.ViewExists(ViewName); } catch { }
            try { await async.ViewExistsAsync(ViewName); } catch { }
        }
        finally
        {
            connector.OnException -= onException;
            connector.OnInit -= onInit;
        }

        Output.WriteLine($"MEASURE {ProviderName} | probe of a missing view | OnException {reported.Count} | OnInit {inits} | escapes {connector.SchemaEscapes.Count - escapesBefore}");
        reported.Should().BeEmpty();
        inits.Should().Be(0);
        connector.SchemaEscapes.Count.Should().Be(escapesBefore);
    }

    [Fact]
    public async Task An_Auto_read_of_a_view_that_does_not_exist_reads_on_the_fly()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;
        var view = DataBase.LoadView(typeof(T901PView));

        var calls = new (string Name, Func<Task<object?>> Call)[]
        {
            ("SelectView (sync)", () => Task.FromResult<object?>(string.Join(",", Logins(connector)))),
            ("SelectCount (sync)", () => Task.FromResult<object?>(connector.SelectCount(view))),
            ("SelectAsync", async () => string.Join(",", await LoginsAsync(async))),
            ("SelectCountAsync", async () => await async.SelectCountAsync(view)),
        };
        var failures = new List<string>();
        foreach (var (name, call) in calls)
        {
            var outcome = await Outcome(call);
            Output.WriteLine($"MEASURE {ProviderName} | Auto, view never created | {name} | {outcome}");
            if (outcome != "a,b" && outcome != "2") failures.Add($"{name}: {outcome}");
        }
        failures.Should().BeEmpty("Auto mode reads on the fly when the view does not exist");
    }

    /// <summary>
    /// Whether <c>CreateView</c> works without the provider's View package. TASK-901 measured the base DDL
    /// (<c>CREATE OR REPLACE VIEW</c>) rejected by SQLite and SQL Server and let those two opt out here; TASK-917 renders
    /// it per provider (the connector's dialect), so every provider is held to it and nothing overrides this any more.
    /// </summary>
    protected virtual bool CreatesViewsWithoutViewPackage => true;

    /// <summary>
    /// TASK-917: <c>CreateView</c> without the View package creates the view on every provider, and creating it again
    /// replaces it rather than failing.
    /// </summary>
    [Fact]
    public async Task CreateView_creates_and_replaces_a_view_without_the_View_package()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;
        DropViewOutside(connector);

        var first = await Outcome(() => { connector.CreateView(typeof(T901PView)); return Task.FromResult<object?>("created"); });
        Output.WriteLine($"MEASURE {ProviderName} | CreateView, no View package | {first}");
        first.Should().Be("created");
        connector.ViewExists(ViewName).Should().BeTrue();

        var again = await Outcome(async () => { await async.CreateViewAsync(typeof(T901PView)); return "replaced"; });
        Output.WriteLine($"MEASURE {ProviderName} | CreateViewAsync over an existing view | {again}");
        again.Should().Be("replaced");
        string.Join(",", Logins(connector)).Should().Be("a,b");
        DropViewOutside(connector);
    }

    /// <summary>
    /// TASK-917: the probe asks for a VIEW. The base probe selected from the name, so a table with that name answered
    /// "the view exists" and Auto mode would read the table as if it were the view.
    /// </summary>
    [Fact]
    public async Task A_table_named_like_the_view_is_not_a_view()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;
        const string tableName = "T917NotAView";
        try { connector.DoCommand(c => c.CommandText = "DROP TABLE " + connector.QuoteIdentifier(tableName), c => c.ExecuteNonQuery()); } catch { }
        connector.DoCommand(c => c.CommandText = "CREATE TABLE " + connector.QuoteIdentifier(tableName) + " (Id INT)", c => c.ExecuteNonQuery());
        connector.ClearViewExistsCache();
        try
        {
            var sync = connector.ViewExists(tableName);
            var asyncAnswer = await async.ViewExistsAsync(tableName);
            Output.WriteLine($"MEASURE {ProviderName} | probe of a TABLE named {tableName} | sync {sync}, async {asyncAnswer}");
            sync.Should().BeFalse("a table is not a view");
            asyncAnswer.Should().BeFalse("a table is not a view");
        }
        finally
        {
            connector.DoCommand(c => c.CommandText = "DROP TABLE " + connector.QuoteIdentifier(tableName), c => c.ExecuteNonQuery());
            connector.ClearViewExistsCache();
        }
    }

    /// <summary>
    /// TASK-917: probing a missing view inside an ambient transaction leaves the transaction usable. The base probe
    /// selected from the missing name and swallowed the error; PostgreSQL aborts a transaction on any error, so the next
    /// statement in it failed (25P02), suspected by TASK-901 and measured here.
    /// </summary>
    [Fact]
    public void Probing_a_missing_view_inside_a_transaction_leaves_it_usable()
    {
        if (!Ready()) return;
        var connector = Connector();
        DropViewOutside(connector);
        connector.ClearViewExistsCache();

        using var connection = connector.CreateConnection(connector.Settings);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        string outcome;
        using (AmbientSqlTransaction.Enter(connector.Settings.GetId(), connection, transaction))
        {
            var exists = connector.ViewExists(ViewName);
            try
            {
                connector.Insert(typeof(T901PSource), new T901PSource { Guid = Guid.NewGuid(), Login = "tx", Amount = 9 });
                outcome = $"probe {exists}, then an insert in the same transaction succeeded";
            }
            catch (Exception ex)
            {
                outcome = $"probe {exists}, then the insert failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
            }
        }
        transaction.Rollback();
        Output.WriteLine($"MEASURE {ProviderName} | probe of a missing view inside a transaction | {outcome}");
        outcome.Should().Be("probe False, then an insert in the same transaction succeeded");
    }

    /// <summary>
    /// <c>CreateViewIfNotExists</c> of a missing view gets past the probe and runs <c>CreateView</c>; where that DDL is
    /// accepted the view exists afterwards.
    /// </summary>
    [Fact]
    public async Task CreateViewIfNotExists_gets_past_the_probe_of_a_missing_view()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;

        foreach (var (name, call) in new (string, Func<Task<object?>>)[]
        {
            ("CreateViewIfNotExists", () => { connector.CreateViewIfNotExists(typeof(T901PView)); return Task.FromResult<object?>("created"); }),
            ("CreateViewIfNotExistsAsync", async () => { await async.CreateViewIfNotExistsAsync(typeof(T901PView)); return "created"; }),
        })
        {
            DropViewOutside(connector);
            var outcome = await Outcome(call);
            Output.WriteLine($"MEASURE {ProviderName} | {name}, view missing | {outcome}");
            outcome.Should().NotContain("'" + ViewName + "'", "the probe answers, so the failure, if any, is the CREATE VIEW's");
            if (!CreatesViewsWithoutViewPackage) continue;
            outcome.Should().Be("created");
            connector.ViewExists(ViewName).Should().BeTrue();
        }
    }

    /// <summary>Only "no such view" is an answer; failing to reach the database is not one (rule 26).</summary>
    [Fact]
    public async Task The_probe_reports_an_error_that_is_not_a_missing_view()
    {
        if (!Ready()) return;
        var connector = UnreachableConnector();
        var async = (AbstractAsyncConnector)connector;

        var sync = await Outcome(() => Task.FromResult<object?>(connector.ViewExists(ViewName)));
        var asynchronous = await Outcome(async () => await async.ViewExistsAsync(ViewName));
        Output.WriteLine($"MEASURE {ProviderName} | unreachable database | ViewExists | {sync}");
        Output.WriteLine($"MEASURE {ProviderName} | unreachable database | ViewExistsAsync | {asynchronous}");
        sync.Should().NotBe("False");
        sync.Should().NotBe("True");
        asynchronous.Should().NotBe("False");
        asynchronous.Should().NotBe("True");
    }

    /// <summary>
    /// TASK-901 item 3 — an Auto view dropped outside the connector. Auto mode caches a positive probe, so the first read
    /// after the drop still targets the view and reports it missing (TASK-891: never an empty answer). That read forgets
    /// the cached answer, so the next read probes again and reads on the fly. Before, every read failed until
    /// <c>InvalidateViewExistsCache</c> was called.
    /// </summary>
    [Fact]
    public async Task An_Auto_view_dropped_outside_the_connector_is_probed_again_after_the_read_that_reports_it()
    {
        if (!Ready()) return;
        var connector = Connector();
        var async = (AbstractAsyncConnector)connector;
        var view = DataBase.LoadView(typeof(T901PView));
        CreateViewOutside(connector);

        var reads = new (string Name, Func<Task<object?>> Call)[]
        {
            ("SelectView (sync)", () => Task.FromResult<object?>(string.Join(",", Logins(connector)))),
            ("SelectCount (sync)", () => Task.FromResult<object?>(connector.SelectCount(view))),
            ("SelectAsync", async () => string.Join(",", await LoginsAsync(async))),
            ("SelectCountAsync", async () => await async.SelectCountAsync(view)),
        };
        var failures = new List<string>();
        foreach (var (name, call) in reads)
        {
            CreateViewOutside(connector);
            var primed = await Statements(connector, async () => await call());
            ReadThePersistentView(primed).Should().BeTrue($"{name}: the view exists, so Auto mode reads it and caches that");
            DropViewOutside(connector);

            string first = "", second = "";
            var firstSent = await Statements(connector, async () => first = await Outcome(call));
            var secondSent = await Statements(connector, async () => second = await Outcome(call));
            Output.WriteLine($"MEASURE {ProviderName} | Auto view dropped outside | {name} | first read: {first} | next read: {second}");

            if (!ReadThePersistentView(firstSent) || first == "a,b" || first == "2")
                failures.Add($"{name}: the first read after the drop should target the cached view and report it, was {first}");
            if (second != "a,b" && second != "2")
                failures.Add($"{name}: the next read should probe again and read on the fly, was {second}");
        }
        failures.Should().BeEmpty();
    }

    /// <summary>A view that exists keeps its cached answer: repeated Auto reads probe once.</summary>
    [Fact]
    public async Task An_Auto_view_that_exists_is_probed_once()
    {
        if (!Ready()) return;
        var connector = Connector();
        var view = DataBase.LoadView(typeof(T901PView));
        CreateViewOutside(connector);
        var probes = 0;
        var sent = await Statements(connector, () =>
        {
            for (var i = 0; i < 3; i++)
            {
                Logins(connector).Should().Equal("a", "b");
                connector.SelectCount(view).Should().Be(2);
            }
            return Task.CompletedTask;
        });
        probes = sent.Count(s => IsViewProbe(s));
        Output.WriteLine($"MEASURE {ProviderName} | Auto view exists | 6 reads | probes {probes} | statements {sent.Count}");
        probes.Should().Be(1);
        ReadThePersistentView(sent).Should().BeTrue();
    }

    /// <summary>
    /// Whether the server lets a table under a plain view be dropped, leaving the view in place (SQLite and SQL Server;
    /// PostgreSQL refuses without CASCADE, MySQL leaves the view invalid with another error).
    /// </summary>
    protected virtual bool DropsSourceTableUnderAView => false;

    /// <summary>
    /// The cached answer is forgotten only when the error names the view (rule 13): a persistent read that fails because a
    /// SOURCE table is missing leaves Auto mode on the view, so the next read targets the view again, without a probe.
    /// </summary>
    [Fact]
    public async Task A_missing_source_table_does_not_make_Auto_mode_forget_the_view()
    {
        if (!Ready() || !DropsSourceTableUnderAView) return;
        var connector = Connector();
        CreateViewOutside(connector);
        Logins(connector).Should().Equal("a", "b");
        connector.DropTable(new[] { typeof(T901PTag) });

        string first = "", second = "";
        await Statements(connector, async () => first = await Outcome(() => Task.FromResult<object?>(string.Join(",", Logins(connector)))));
        var sent = await Statements(connector, async () => second = await Outcome(() => Task.FromResult<object?>(string.Join(",", Logins(connector)))));
        Output.WriteLine($"MEASURE {ProviderName} | Auto view, source table dropped | first read: {first} | next read: {second} | probes {sent.Count(s => IsViewProbe(s))}");

        second.Should().Contain("T901PTags", "the read still targets the view, which reports its missing source table");
        sent.Should().NotContain(s => IsViewProbe(s), "the view was not forgotten, so nothing probes");
    }
}
