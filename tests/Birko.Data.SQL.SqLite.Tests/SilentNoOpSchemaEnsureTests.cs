using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-290 Round 2 — <b>can a store's schema-ensure complete without issuing any DDL and without
/// raising?</b> That is the one candidate Round 1's probes left open for the escape: everything else
/// requires either a table that was removed or a create that was never committed, and Round 1 measured
/// that a committed create cannot be invisible while a create whose commit failed is never recorded.
///
/// <para>The path is visible in the code: <c>CreateTable(Type[])</c> → <c>DataBase.LoadTables</c>, which
/// <b>skips</b> a type whose <c>LoadTable</c> returns null or whose field set is empty; then
/// <c>CreateTable(IEnumerable&lt;Tables.Table&gt;)</c> does nothing for an empty sequence. So
/// <c>InitCoreAsync</c> returns normally, the store records itself initialised, and no table exists.</para>
///
/// <para>⚠ <b>The prediction is falsifiable and it is what makes this worth ten minutes:</b> on that path
/// <c>RecordTableCreated</c> is not reached either, so such a failure must annotate as the <b>benign</b>
/// "NO recorded CREATE TABLE" branch and cannot be the anomaly. If that holds, this is not TASK-290's
/// mechanism — but it is its own defect, and a worse-behaved one.</para>
/// </summary>
public class SilentNoOpSchemaEnsureTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private static int _seq;

    public SilentNoOpSchemaEnsureTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-noop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>No process-wide a process-wide pool clear — see [[TASK-276]].</remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>
    /// The consumer mistake this models: an entity with <b>no</b> <c>[Table]</c> attribute and no
    /// <c>ModelMapRegistry</c> mapping. <c>DataBase.LoadTable</c> answers null for it.
    /// </summary>
    public class UnmappedRow : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    /// <summary>The control: identical, but declared.</summary>
    [Table("MappedRows")]
    public class MappedRow : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    private SqLiteSettings Fresh() => new SqLiteSettings(_root, $"noop{Interlocked.Increment(ref _seq)}.db");

    private static SqLiteConnector Connector(SqLiteSettings settings)
        => (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

    private static int TableCount(SqLiteSettings settings)
    {
        using var db = new SqliteConnection(settings.GetConnectionString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void An_unmapped_entity_has_no_table_metadata_at_all()
    {
        DataBase.LoadTable(typeof(UnmappedRow)).Should().BeNull(
            "the premise: no [Table] attribute and no fluent mapping, so there is nothing to create");
        DataBase.LoadTable(typeof(MappedRow)).Should().NotBeNull();
    }

    /// <summary>
    /// The measurement. Whatever it says, it is worth having written down: either schema-ensure refuses,
    /// or a store reports itself initialised over a table that does not exist.
    /// </summary>
    [Fact]
    public async Task What_a_store_over_an_unmapped_entity_actually_does()
    {
        var settings = Fresh();
        var connector = Connector(settings);
        var store = new AsyncSQLiteStore<UnmappedRow>();
        store.SetSettings(settings);

        Exception? initFailure = null;
        try { await store.InitAsync(); }
        catch (Exception ex) { initFailure = ex; }

        _out.WriteLine($"InitAsync threw: {initFailure?.GetType().Name ?? "(nothing)"}");
        _out.WriteLine($"tables in file: {TableCount(settings)}");
        _out.WriteLine($"TablesCreated: [{string.Join(", ", connector.TablesCreated.Keys)}]");

        long count = -1;
        Exception? countFailure = null;
        try { count = await store.CountAsync(); }
        catch (Exception ex) { countFailure = ex; }
        _out.WriteLine($"CountAsync: {count} (threw: {countFailure?.GetType().Name ?? "(nothing)"})");

        Guid written = Guid.Empty;
        Exception? writeFailure = null;
        try { written = await store.CreateAsync(new UnmappedRow { Guid = Guid.NewGuid(), Value = "x" }); }
        catch (Exception ex) { writeFailure = ex; }
        _out.WriteLine($"CreateAsync returned: {written} (threw: {writeFailure?.GetType().Name ?? "(nothing)"})");

        _out.WriteLine($"escapes={connector.SchemaEscapes.Count} generation={connector.SchemaGeneration}");
        foreach (var e in connector.SchemaEscapes)
        {
            _out.WriteLine($"  ESCAPE [{string.Join(", ", e.TableNames)}] {e.Annotation}");
        }

        // The prediction under test: this path cannot be TASK-290's anomaly, because nothing was recorded
        // as created. Asserted rather than reasoned, because the whole point of Round 2 is that thirteen
        // hypotheses died by code reading.
        connector.SchemaEscapes.Should().NotContain(e => e.Annotation != null
            && e.Annotation.Contains("but this connector already created it"),
            "on this path RecordTableCreated is never reached, so a failure here must read as the BENIGN "
            + "first-touch branch — which is what rules it out as the escape's mechanism");
    }

    /// <summary>The control, so the fixture is not the thing being measured.</summary>
    [Fact]
    public async Task A_mapped_entity_gets_its_table_and_stores_rows()
    {
        var settings = Fresh();
        var store = new AsyncSQLiteStore<MappedRow>();
        store.SetSettings(settings);

        await store.CreateAsync(new MappedRow { Guid = Guid.NewGuid(), Value = "x" });

        (await store.CountAsync()).Should().Be(1);
        TableCount(settings).Should().BeGreaterThan(0);
    }
}
