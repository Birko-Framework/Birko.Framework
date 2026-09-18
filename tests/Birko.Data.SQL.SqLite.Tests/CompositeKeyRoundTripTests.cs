using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-303 — a composite primary key end to end, because declaring one is only half the point.
///
/// <para>
/// <c>UpdateCore</c> and <c>DeleteCore</c> build one condition per field returned by
/// <c>DataBase.GetPrimaryFields</c>, so a composite key should produce a two-column <c>WHERE</c>. That
/// "should" is what this file replaces with a measurement: the key is what makes update and delete address
/// a single row, and a key that exists in the DDL but is not used by the verbs would be decorative.
/// </para>
///
/// <para>
/// ⚠ <b>The discriminating fixture is two rows that share the first key column.</b> A test with distinct
/// Guids would pass even if only the first column were used — which is exactly the failure a composite key
/// is meant to prevent, so it must be the fixture rather than an afterthought.
/// </para>
/// </summary>
public class CompositeKeyRoundTripTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _connectionStrings = new();
    private static int _seq;

    public CompositeKeyRoundTripTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-compkey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // TASK-276 -- precise, never process-wide.
        foreach (var cs in _connectionStrings)
        {
            try { using var probe = new SqliteConnection(cs); SqliteConnection.ClearPool(probe); } catch { }
        }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>
    /// A telemetry entity keyed on <c>(Guid, Ts)</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>It derives from <c>Birko.Data.Models.AbstractModel</c> and declares its own key, NOT from
    /// <c>AbstractDatabaseModel</c> — and that is a measured limitation rather than a style choice.</b>
    /// <c>AbstractDatabaseModel</c> puts <c>[UniqueField]</c> <i>and</i> <c>[PrimaryField]</c> on
    /// <c>Guid</c>, so its subclasses carry a standalone <c>UNIQUE (Guid)</c> that forbids two rows sharing
    /// a Guid whatever the primary key says. Measured: the first version of this fixture derived from it
    /// and every insert of a second row failed with
    /// <c>SQLite Error 19: 'UNIQUE constraint failed: Readings.Guid'</c>.
    /// <para>
    /// So TASK-303 makes a composite key expressible, and an entity inheriting
    /// <c>AbstractDatabaseModel</c> still cannot use one. That gap is [[TASK-304]].
    /// </para>
    /// </remarks>
    [Table("Readings")]
    public class Reading : Birko.Data.Models.AbstractModel
    {
        [PrimaryField]
        public override Guid? Guid { get; set; }

        [PrimaryField]
        [RequiredField]
        public DateTime Ts { get; set; }

        public double Value { get; set; }
    }

    private SqLiteSettings NewDatabase()
    {
        var settings = new SqLiteSettings(_root, $"comp{Interlocked.Increment(ref _seq)}.db") { CommandTimeout = 5 };
        _connectionStrings.Add(settings.GetConnectionString());
        return settings;
    }

    private static AsyncSQLiteStore<Reading> Store(SqLiteSettings settings)
    {
        var store = new AsyncSQLiteStore<Reading>();
        store.SetSettings(settings);
        return store;
    }

    /// <summary>Two rows sharing a Guid, distinguished only by the second key column.</summary>
    private static (Reading a, Reading b) TwoRowsSharingTheFirstKeyColumn(Guid id)
        => (new Reading { Guid = id, Ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), Value = 1 },
            new Reading { Guid = id, Ts = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), Value = 2 });

    [Fact]
    public async Task A_composite_keyed_entity_round_trips()
    {
        var settings = NewDatabase();
        var store = Store(settings);
        var id = Guid.NewGuid();
        var (a, b) = TwoRowsSharingTheFirstKeyColumn(id);

        await store.CreateAsync(a);
        await store.CreateAsync(b);

        var all = (await store.ReadAsync(x => x.Guid == id, null, null, null, default)).ToList();
        all.Should().HaveCount(2, "both rows differ in the second key column, so both are distinct keys");
    }

    /// <summary>
    /// The load-bearing assertion: an update must touch <b>one</b> row. If only the first key column were
    /// used, both rows would be rewritten and this fails — which is the whole reason the fixture shares a
    /// Guid.
    /// </summary>
    [Fact]
    public async Task An_update_addresses_one_row_using_BOTH_key_columns()
    {
        var settings = NewDatabase();
        var store = Store(settings);
        var id = Guid.NewGuid();
        var (a, b) = TwoRowsSharingTheFirstKeyColumn(id);

        await store.CreateAsync(a);
        await store.CreateAsync(b);

        a.Value = 99;
        await store.UpdateAsync(a);

        var rows = (await store.ReadAsync(x => x.Guid == id, null, null, null, default))
                   .OrderBy(x => x.Ts).ToList();

        rows.Should().HaveCount(2);
        rows[0].Value.Should().Be(99, "the row whose full key was supplied is updated");
        rows[1].Value.Should().Be(2, "and its sibling, sharing only the first key column, is untouched");
    }

    /// <summary>The same for delete — the verb that would silently remove a sibling row.</summary>
    [Fact]
    public async Task A_delete_removes_one_row_using_BOTH_key_columns()
    {
        var settings = NewDatabase();
        var store = Store(settings);
        var id = Guid.NewGuid();
        var (a, b) = TwoRowsSharingTheFirstKeyColumn(id);

        await store.CreateAsync(a);
        await store.CreateAsync(b);

        await store.DeleteAsync(a);

        var rows = (await store.ReadAsync(x => x.Guid == id, null, null, null, default)).ToList();

        rows.Should().ContainSingle("only the row whose full key was supplied is removed")
            .Which.Ts.Should().Be(b.Ts);
    }

    /// <summary>
    /// And the key is really enforced by the database, not merely used by the verbs: the same
    /// <c>(Guid, Ts)</c> twice must be refused.
    /// </summary>
    [Fact]
    public async Task The_composite_key_is_enforced_by_the_database()
    {
        var settings = NewDatabase();
        var store = Store(settings);
        var id = Guid.NewGuid();
        var ts = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await store.CreateAsync(new Reading { Guid = id, Ts = ts, Value = 1 });

        Func<Task> duplicate = async () =>
            await store.CreateAsync(new Reading { Guid = id, Ts = ts, Value = 2 });

        await duplicate.Should().ThrowAsync<Exception>(
            "a composite PRIMARY KEY must reject a duplicate of the whole key");
    }
}
