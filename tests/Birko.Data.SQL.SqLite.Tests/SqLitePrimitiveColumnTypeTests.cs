using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-H037 — the DDL half. <c>long</c> / <c>short</c> / <c>double</c> / <c>float</c> / <c>byte[]</c>
/// produced no field at all, so these <c>ConvertType</c> arms — which already existed — were unreachable
/// from an attribute-driven model. No live database needed; <c>ConvertType</c> / <c>FieldDefinition</c>
/// are pure.
/// <para>
/// Also covers the SQLite half of CR-H087, fixed in the same pass: <c>DbType.Single</c> was grouped with
/// the integral types and declared a <c>float</c> column as <c>INTEGER</c>. PostgreSQL and MSSql had both
/// already fixed exactly that; SQLite had not, and it is the reference/test provider.
/// </para>
/// <para>
/// Each case goes through <c>DataBase.LoadTable</c> rather than constructing the field class by hand —
/// a hand-built field survives a dispatch-only revert, so such a test cannot witness this fix.
/// </para>
/// </summary>
public class SqLitePrimitiveColumnTypeTests
{
    [Table("SqLitePrimitiveSpread")]
    public class Sample : AbstractLogModel
    {
        public long Ticks { get; set; }
        public short Small { get; set; }
        public double Ratio { get; set; }
        public float Single { get; set; }
        public byte[]? Blob { get; set; }
    }

    private static SqLiteConnector NewConnector()
        => new(new SqLiteSettings(System.IO.Path.GetTempPath(), "coltypes.db"));

    private static string DefinitionFor(string property)
    {
        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Sample));
        var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == property);
        field.Should().NotBeNull($"'{property}' must map to a column at all — SH-H037 was that it did not");
        return NewConnector().FieldDefinition(field!);
    }

    [Fact]
    public void Long_DeclaresInteger()
        => DefinitionFor(nameof(Sample.Ticks)).Should().Be("Ticks INTEGER NOT NULL");

    [Fact]
    public void Short_DeclaresInteger()
        => DefinitionFor(nameof(Sample.Small)).Should().Be("Small INTEGER NOT NULL");

    [Fact]
    public void Double_DeclaresReal()
        => DefinitionFor(nameof(Sample.Ratio)).Should().Be("Ratio REAL NOT NULL");

    [Fact]
    public void Float_DeclaresReal_NotInteger()
    {
        // CR-H087, SQLite half: an INTEGER declaration rounds every whole-valued float and misdeclares
        // the column's storage class. Two-sided on purpose — a Contain("REAL") alone would also pass
        // against a NUMERIC/INTEGER answer that happened to carry the substring.
        var definition = DefinitionFor(nameof(Sample.Single));

        definition.Should().Be("Single REAL NOT NULL");
        definition.Should().NotContain("INTEGER");
    }

    [Fact]
    public void ByteArray_DeclaresBlob_AndIsNullableByDefault()
    {
        // byte[] is a reference type, so it follows StringField's convention: nullable unless the model
        // says otherwise. An empty array and a null are distinct stored values.
        DefinitionFor(nameof(Sample.Blob)).Should().Be("Blob BLOB");
    }
}
