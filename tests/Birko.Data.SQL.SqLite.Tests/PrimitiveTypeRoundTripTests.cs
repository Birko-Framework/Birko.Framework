using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-H037, end-to-end. Before the fix a model carrying <c>long</c> / <c>short</c> / <c>double</c> /
/// <c>float</c> / <c>byte[]</c> got a CREATE TABLE without those columns: <c>Write()</c> never emitted
/// them and <c>Read()</c> never restored them, so every such value was dropped on save — no exception, no
/// log entry, and a subsequent read returned the type's default as if that were the stored value.
/// <para>
/// These tests write real rows through a real store and read them back, because that is the only place
/// the defect was observable: a column-mapping assertion alone would not catch a field whose
/// <c>Read</c> materialises the wrong CLR type or narrows the value.
/// </para>
/// </summary>
public class PrimitiveTypeRoundTripTests : IDisposable
{
    private readonly string _root;

    public PrimitiveTypeRoundTripTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-prim-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("PrimRows")]
    public class PrimRow : AbstractLogModel
    {
        public long Ticks { get; set; }
        public long? NullableTicks { get; set; }
        public short Small { get; set; }
        public short? NullableSmall { get; set; }
        public double Ratio { get; set; }
        public double? NullableRatio { get; set; }
        public float Single { get; set; }
        public float? NullableSingle { get; set; }
        public byte[]? Blob { get; set; }
        public string Label { get; set; } = null!;
    }

    private AsyncSQLiteStore<PrimRow> NewStore()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "prim.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(PrimRow) });

        var store = new AsyncSQLiteStore<PrimRow>();
        store.SetSettings(new SqLiteSettings(_root, "prim.db"));
        return store;
    }

    [Fact]
    public async Task EveryPreviouslyDroppedType_SurvivesAWriteAndRead()
    {
        var store = NewStore();
        var blob = new byte[] { 0x00, 0x01, 0xFE, 0xFF, 0x7F, 0x80 };

        await store.CreateAsync(new PrimRow
        {
            Ticks = 9_007_199_254_740_993L, // > 2^53: also proves it did not detour through a double
            NullableTicks = -42L,
            Small = -12345,
            NullableSmall = 999,
            Ratio = 3.141592653589793d,
            NullableRatio = -0.5d,
            Single = 2.5f,
            NullableSingle = -1.25f,
            Blob = blob,
            Label = "row",
        });

        var read = (await store.ReadAsync(x => x.Label == "row")).Single();

        read.Ticks.Should().Be(9_007_199_254_740_993L);
        read.NullableTicks.Should().Be(-42L);
        read.Small.Should().Be(-12345);
        read.NullableSmall.Should().Be(999);
        read.Ratio.Should().Be(3.141592653589793d);
        read.NullableRatio.Should().Be(-0.5d);
        read.Single.Should().Be(2.5f);
        read.NullableSingle.Should().Be(-1.25f);
        read.Blob.Should().Equal(blob);
    }

    [Fact]
    public async Task LongBoundaries_RoundTripExactly()
    {
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Ticks = long.MinValue, Label = "min" });
        await store.CreateAsync(new PrimRow { Ticks = long.MaxValue, Label = "max" });

        (await store.ReadAsync(x => x.Label == "min")).Single().Ticks.Should().Be(long.MinValue);
        (await store.ReadAsync(x => x.Label == "max")).Single().Ticks.Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task ShortBoundaries_RoundTripExactly()
    {
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Small = short.MinValue, Label = "min" });
        await store.CreateAsync(new PrimRow { Small = short.MaxValue, Label = "max" });

        (await store.ReadAsync(x => x.Label == "min")).Single().Small.Should().Be(short.MinValue);
        (await store.ReadAsync(x => x.Label == "max")).Single().Small.Should().Be(short.MaxValue);
    }

    [Fact]
    public async Task DoubleExtremes_RoundTripExactly()
    {
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Ratio = double.MaxValue, Label = "max" });
        await store.CreateAsync(new PrimRow { Ratio = double.Epsilon, Label = "eps" });
        // A whole-valued double is the case a NUMERIC/INTEGER-affinity column silently narrows.
        await store.CreateAsync(new PrimRow { Ratio = 2.0d, Label = "whole" });

        (await store.ReadAsync(x => x.Label == "max")).Single().Ratio.Should().Be(double.MaxValue);
        (await store.ReadAsync(x => x.Label == "eps")).Single().Ratio.Should().Be(double.Epsilon);
        (await store.ReadAsync(x => x.Label == "whole")).Single().Ratio.Should().Be(2.0d);
    }

    [Fact]
    public async Task FloatWithAFraction_RoundTripsWithoutTruncation()
    {
        // The direct consequence of DbType.Single having been declared INTEGER on SQLite: a fractional
        // float would round. 0.5 is exactly representable in binary, so a failure here is truncation,
        // not float error.
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Single = 1.5f, Label = "frac" });

        (await store.ReadAsync(x => x.Label == "frac")).Single().Single.Should().Be(1.5f);
    }

    [Fact]
    public async Task EmptyAndNullBlob_AreDistinctStoredValues()
    {
        // If both collapsed to NULL the column would be lossy in a way no round-trip of non-empty data
        // would reveal.
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Blob = Array.Empty<byte>(), Label = "empty" });
        await store.CreateAsync(new PrimRow { Blob = null, Label = "null" });

        (await store.ReadAsync(x => x.Label == "empty")).Single().Blob.Should().NotBeNull().And.BeEmpty();
        (await store.ReadAsync(x => x.Label == "null")).Single().Blob.Should().BeNull();
    }

    [Fact]
    public async Task NullablesLeftUnset_ReadBackAsNull_NotAsZero()
    {
        // Distinguishes "never written" from "written as the type default" — the two were
        // indistinguishable before the fix, because neither reached the database.
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Label = "unset" });

        var read = (await store.ReadAsync(x => x.Label == "unset")).Single();
        read.NullableTicks.Should().BeNull();
        read.NullableSmall.Should().BeNull();
        read.NullableRatio.Should().BeNull();
        read.NullableSingle.Should().BeNull();
        read.Blob.Should().BeNull();
    }

    [Fact]
    public async Task ThePreviouslyDroppedColumnsAreFilterable()
    {
        // A column that exists but cannot be filtered on is only half-restored; this also proves the
        // values reached the database rather than being reconstructed client-side.
        var store = NewStore();

        await store.CreateAsync(new PrimRow { Ticks = 100L, Ratio = 1.5d, Label = "a" });
        await store.CreateAsync(new PrimRow { Ticks = 200L, Ratio = 2.5d, Label = "b" });

        (await store.ReadAsync(x => x.Ticks > 150L)).Single().Label.Should().Be("b");
        (await store.ReadAsync(x => x.Ratio < 2.0d)).Single().Label.Should().Be("a");
    }
}
