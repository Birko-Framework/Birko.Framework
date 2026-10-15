using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SQLite.IndexManagement;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-154, the medium findings in <c>schema-index-and-ddl</c>, against a real SQLite database: what a model maps
/// to, what is read back, and what the index manager reports.
/// </summary>
public class SchemaDdlTriageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-sd154-{Guid.NewGuid():N}");
    private int _db;

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private Data.SQL.Connectors.SqLiteConnector Connector()
        => (Data.SQL.Connectors.SqLiteConnector)new SqLiteStoreFactory(
            new SqLiteStoreFactoryOptions { Location = _root, Name = $"s{++_db}.db" }).GetConnector();

    private static T Single<T>(Data.SQL.Connectors.SqLiteConnector c)
        => c.Select(typeof(T), (LambdaExpression?)null).Cast<T>().Single();

    public enum Small : byte { A = 1, B = 2 }
    public enum Big : long { A = 1 }

    [Table("Sd154Char")] public class CharRow : AbstractModel { public char Letter { get; set; } }
    [Table("Sd154Get")] public class GetRow : AbstractModel { public string? First { get; set; } public string Display => First + "!"; }
    [Table("Sd154Small")] public class SmallRow : AbstractModel { public Small S { get; set; } public Small? N { get; set; } }
    [Table("Sd154Big")] public class BigRow : AbstractModel { public Big L { get; set; } }
    public class TenantBase : AbstractModel { [IndexedField("IX_Sd154_Tenant")] public int Tenant { get; set; } }
    [Table("Sd154DerA")] public class DerA : TenantBase { public int A { get; set; } }
    [Table("Sd154IgIdx")] public class IgnoredIndexed : AbstractModel { [IgnoreField, IndexedField("IX_Sd154_Ignored")] public int Gone { get; set; } public int Kept { get; set; } }
    [Table("Sd154Tie")] public class Tie : AbstractModel { [IndexedField("IX_Sd154_Tie")] public int Zeta { get; set; } [IndexedField("IX_Sd154_Tie")] public int Alpha { get; set; } }
    public class NoTable : AbstractModel { public int X { get; set; } }

    [Table("Sd154One")] public class One : AbstractModel { [IndexedField("IX_Sd154_Shared")] public int Code { get; set; } }
    [Table("Sd154Two")] public class Two : AbstractModel { public int Code { get; set; } }

    [Table("Sd154Named")]
    public class Named : AbstractModel
    {
        [IndexedField("IX_UNIQUE_Sd154_Code")] public int Code { get; set; }
        [IndexedField("IX_Sd154_Desc", 0, true)] public int Rank { get; set; }
        [IndexedField("UX_Sd154_Real", 0, false, true)] public int Number { get; set; }
    }

    // ---- fields ----

    /// <summary>SH-M221: a char column read the string back into the char property, which threw for every row.</summary>
    [Fact]
    public void A_char_round_trips()
    {
        var c = Connector();
        c.CreateTable(new[] { typeof(CharRow) });
        c.Insert(new CharRow { Guid = Guid.NewGuid(), Letter = 'Q' });
        var row = Single<CharRow>(c);
        row.Letter.Should().Be('Q');
    }

    /// <summary>SH-M240: a get-only property became a column, and every read failed to set it.</summary>
    [Fact]
    public void A_get_only_property_is_no_column()
    {
        DataBase.LoadTable(typeof(GetRow)).GetFieldByPropertyName(nameof(GetRow.Display)).Should().BeNull();
        var c = Connector();
        c.CreateTable(new[] { typeof(GetRow) });
        c.Insert(new GetRow { Guid = Guid.NewGuid(), First = "a" });
        Single<GetRow>(c).Display.Should().Be("a!");
    }

    /// <summary>SH-M241: `(int)val` unboxes only an int-backed enum, so a byte-backed one threw on every write.</summary>
    [Fact]
    public void A_byte_backed_enum_round_trips()
    {
        var c = Connector();
        c.CreateTable(new[] { typeof(SmallRow) });
        c.Insert(new SmallRow { Guid = Guid.NewGuid(), S = Small.B, N = Small.A });
        var row = Single<SmallRow>(c);
        row.S.Should().Be(Small.B);
        row.N.Should().Be(Small.A);
    }

    [Fact]
    public void A_long_backed_enum_is_refused_rather_than_truncated()
    {
        var act = () => DataBase.LoadTable(typeof(BigRow));
        act.Should().Throw<FieldAttributeException>().WithMessage("*Int64*");
    }

    // ---- table load ----

    /// <summary>SH-M222: an unmapped type was a null table, so a read answered 0 rows instead of an error.</summary>
    [Fact]
    public void An_unmapped_type_is_refused_by_name()
    {
        var act = () => DataBase.LoadTable(typeof(NoTable));
        act.Should().Throw<TableAttributeException>().WithMessage("*NoTable*");
        DataBase.TryLoadTable(typeof(NoTable)).Should().BeNull();
        var read = () => Connector().Select(typeof(NoTable), (LambdaExpression?)null).Cast<NoTable>().ToList();
        read.Should().Throw<TableAttributeException>();
    }

    /// <summary>SH-M242: an inherited [IndexedField] named one index for every derived table; all but the first were skipped.</summary>
    [Fact]
    public void An_indexed_field_on_a_base_class_is_refused()
    {
        var act = () => DataBase.LoadTable(typeof(DerA));
        act.Should().Throw<TableAttributeException>().WithMessage("*TenantBase.Tenant*CompositeIndex*");
    }

    /// <summary>SH-M223: an index on an unmapped property named a column the table lacks, failing far from the declaration.</summary>
    [Fact]
    public void An_indexed_field_on_an_unmapped_property_is_refused()
    {
        var act = () => DataBase.LoadTable(typeof(IgnoredIndexed));
        act.Should().Throw<TableAttributeException>().WithMessage("*Gone*not a mapped column*");
    }

    /// <summary>SH-M224 (pin): tied Order values keep declaration order, now by a stable sort rather than by luck.</summary>
    [Fact]
    public void Columns_with_the_same_order_keep_declaration_order()
        => DataBase.LoadTable(typeof(Tie)).Indexes!["IX_Sd154_Tie"].Columns.Select(x => x.ColumnName).Should().Equal("Zeta", "Alpha");

    // ---- index manager ----

    /// <summary>SH-M225: ExistsAsync ignored the table, so an index of that name on ANOTHER table answered true.</summary>
    [Fact]
    public async Task Exists_asks_about_the_table_it_is_given()
    {
        var c = Connector();
        c.CreateTable(new[] { typeof(One), typeof(Two) });
        var manager = new SqLiteIndexManager(c);
        (await manager.ExistsAsync("IX_Sd154_Shared", "Sd154One")).Should().BeTrue();
        (await manager.ExistsAsync("IX_Sd154_Shared", "Sd154Two")).Should().BeFalse();
    }

    /// <summary>SH-M226: uniqueness was `sql LIKE '%UNIQUE%'`, true for an index merely named UNIQUE. SH-M244: no direction.</summary>
    [Fact]
    public async Task List_reads_uniqueness_and_direction_from_the_index_itself()
    {
        var c = Connector();
        c.CreateTable(new[] { typeof(Named) });
        var listed = (await new SqLiteIndexManager(c).ListAsync("Sd154Named")).ToDictionary(i => i.Name);

        listed["IX_UNIQUE_Sd154_Code"].Unique.Should().BeFalse("its name contains UNIQUE; the index is not unique");
        listed["UX_Sd154_Real"].Unique.Should().BeTrue();
        listed["IX_Sd154_Desc"].Fields.Single().IsDescending.Should().BeTrue();
        listed["IX_UNIQUE_Sd154_Code"].Fields.Single().Name.Should().Be("Code");
    }
}
