using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    /// <summary>
    /// SH-H037 — <c>AbstractField.CreateAbstractField</c> dispatched only on bool / DateTime / decimal /
    /// Guid / int / char / string (+ nullable) and enum. Every other CLR type fell to <c>return null</c>,
    /// which <c>LoadField</c> turned into an empty field set: the property got NO column, was never
    /// written by <c>Write()</c> and never restored by <c>Read()</c> — silent write-side data loss with no
    /// exception and no log entry.
    /// <para>
    /// <c>decimal</c> was mapped, so money was safe; the types that vanished were identifiers
    /// (<c>long</c>), measurements (<c>double</c>/<c>float</c>/<c>short</c>) and blobs (<c>byte[]</c>).
    /// </para>
    /// <para>
    /// The silence was the deeper half of the defect — it guaranteed the NEXT unmapped type would repeat
    /// the bug — so an unsupported type now throws at table load instead of dropping the column.
    /// </para>
    /// </summary>
    public class PrimitiveTypeMappingTests
    {
        [Table("PrimitiveSpread")]
        public class PrimitiveSpreadModel : AbstractLogModel
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

            // The arms that already worked — asserted so the dispatch rewrite cannot regress them.
            public bool Flag { get; set; }
            public int Count { get; set; }
            public decimal Amount { get; set; }
            public string Text { get; set; } = null!;
            public DateTime When { get; set; }
            public Guid Key { get; set; }
        }

        private static AbstractField FieldFor(string propertyName)
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(PrimitiveSpreadModel));
            var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == propertyName);
            field.Should().NotBeNull($"'{propertyName}' must map to a column");
            return field!;
        }

        // ---- the types that produced no column at all -------------------------------------------------

        [Theory]
        [InlineData(nameof(PrimitiveSpreadModel.Ticks), typeof(LongField), DbType.Int64, true)]
        [InlineData(nameof(PrimitiveSpreadModel.NullableTicks), typeof(NullableLongField), DbType.Int64, false)]
        [InlineData(nameof(PrimitiveSpreadModel.Small), typeof(ShortField), DbType.Int16, true)]
        [InlineData(nameof(PrimitiveSpreadModel.NullableSmall), typeof(NullableShortField), DbType.Int16, false)]
        [InlineData(nameof(PrimitiveSpreadModel.Ratio), typeof(DoubleField), DbType.Double, true)]
        [InlineData(nameof(PrimitiveSpreadModel.NullableRatio), typeof(NullableDoubleField), DbType.Double, false)]
        [InlineData(nameof(PrimitiveSpreadModel.Single), typeof(FloatField), DbType.Single, true)]
        [InlineData(nameof(PrimitiveSpreadModel.NullableSingle), typeof(NullableFloatField), DbType.Single, false)]
        [InlineData(nameof(PrimitiveSpreadModel.Blob), typeof(BinaryField), DbType.Binary, false)]
        public void PreviouslyUnmappedType_NowMapsToItsOwnFieldAndDbType(
            string propertyName, Type expectedFieldType, DbType expectedDbType, bool expectedNotNull)
        {
            var field = FieldFor(propertyName);

            field.Should().BeOfType(expectedFieldType);
            field.Type.Should().Be(expectedDbType);
            field.IsNotNull.Should().Be(expectedNotNull);
        }

        [Fact]
        public void EveryPreviouslyDroppedProperty_IsNowAColumnOnTheTable()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(PrimitiveSpreadModel));
            var mapped = table.Fields.Values.Select(f => f.Property?.Name).ToList();

            // Before the fix this list was empty — the CREATE TABLE simply had no such columns.
            mapped.Should().Contain(new[]
            {
                nameof(PrimitiveSpreadModel.Ticks),
                nameof(PrimitiveSpreadModel.NullableTicks),
                nameof(PrimitiveSpreadModel.Small),
                nameof(PrimitiveSpreadModel.NullableSmall),
                nameof(PrimitiveSpreadModel.Ratio),
                nameof(PrimitiveSpreadModel.NullableRatio),
                nameof(PrimitiveSpreadModel.Single),
                nameof(PrimitiveSpreadModel.NullableSingle),
                nameof(PrimitiveSpreadModel.Blob),
            });
        }

        [Fact]
        public void DoubleIsNotRoutedThroughDecimal()
        {
            // decimal is exact base-10, double is binary floating point. Mapping one onto the other would
            // round-trip a value the caller never stored, and would emit the wrong provider column type.
            FieldFor(nameof(PrimitiveSpreadModel.Ratio)).Should().NotBeAssignableTo<DecimalField>();
            FieldFor(nameof(PrimitiveSpreadModel.Amount)).Should().BeOfType<DecimalField>();
        }

        [Fact]
        public void LongIsNotRoutedThroughInteger()
        {
            // An Int32 column silently truncates every id past 2^31.
            FieldFor(nameof(PrimitiveSpreadModel.Ticks)).Should().NotBeAssignableTo<IntegerField>();
            FieldFor(nameof(PrimitiveSpreadModel.Ticks)).Type.Should().Be(DbType.Int64);
        }

        // ---- the arms that already worked, pinned against the dispatch rewrite ------------------------

        [Theory]
        [InlineData(nameof(PrimitiveSpreadModel.Flag), typeof(BooleanField), DbType.Boolean)]
        [InlineData(nameof(PrimitiveSpreadModel.Count), typeof(IntegerField), DbType.Int32)]
        [InlineData(nameof(PrimitiveSpreadModel.Amount), typeof(DecimalField), DbType.Decimal)]
        [InlineData(nameof(PrimitiveSpreadModel.Text), typeof(StringField), DbType.String)]
        [InlineData(nameof(PrimitiveSpreadModel.When), typeof(DateTimeField), DbType.DateTime)]
        [InlineData(nameof(PrimitiveSpreadModel.Key), typeof(GuidField), DbType.Guid)]
        public void AlreadyMappedType_IsUnchanged(string propertyName, Type expectedFieldType, DbType expectedDbType)
        {
            var field = FieldFor(propertyName);

            field.Should().BeOfType(expectedFieldType);
            field.Type.Should().Be(expectedDbType);
        }

        [Table("EnumSpread")]
        public class EnumSpreadModel : AbstractLogModel
        {
            public enum Kind { A, B }
            public Kind Which { get; set; }
            public Kind? MaybeWhich { get; set; }
        }

        [Fact]
        public void EnumStillMapsToInteger()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(EnumSpreadModel));

            table.Fields.Values.First(f => f.Property?.Name == nameof(EnumSpreadModel.Which))
                 .Should().BeOfType<IntegerField>();
            table.Fields.Values.First(f => f.Property?.Name == nameof(EnumSpreadModel.MaybeWhich))
                 .Should().BeOfType<NullableIntegerField>();
        }

        // ---- FieldType.Long / .Double / .Binary now correspond to something real ----------------------

        [Theory]
        [InlineData(nameof(PrimitiveSpreadModel.Ticks), DbType.Int64)]   // FieldType.Long
        [InlineData(nameof(PrimitiveSpreadModel.Ratio), DbType.Double)]  // FieldType.Double
        [InlineData(nameof(PrimitiveSpreadModel.Blob), DbType.Binary)]   // FieldType.Binary
        public void PortableFieldTypeHasAnAttributeDrivenCounterpart(string propertyName, DbType schemaBuilderDbType)
        {
            // Birko.Data.Migrations.SQL's SchemaField already mapped FieldType.Long/.Double/.Binary to
            // these DbTypes, so a migration could create a column the attribute-driven mapper could never
            // bind a property to. The two paths now agree.
            FieldFor(propertyName).Type.Should().Be(schemaBuilderDbType);
        }

        // ---- the silence itself: an unsupported type must fail loudly ---------------------------------

        [Table("Unmappable")]
        public class UnmappableModel : AbstractLogModel
        {
            public IEnumerable<string> Items { get; set; } = null!;
        }

        [Fact]
        public void UnsupportedType_ThrowsAtTableLoad_NamingThePropertyAndItsType()
        {
            var act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(UnmappableModel));

            var ex = act.Should().Throw<Birko.Data.Exceptions.FieldAttributeException>().Which;
            // The message must be actionable without a debugger: which property, which type, what to do.
            ex.Message.Should().Contain(nameof(UnmappableModel.Items));
            ex.Message.Should().Contain("IEnumerable");
            ex.Message.Should().Contain("IgnoreField");
            ex.Message.Should().Contain("NotMapped");
        }

        [Table("Indexed")]
        public class IndexerModel : AbstractLogModel
        {
            private readonly System.Collections.Generic.Dictionary<string, string> _bag = new();

            public string Text { get; set; } = null!;

            // GetProperties enumerates an indexer like any other public instance property.
            public string this[string key]
            {
                get => _bag[key];
                set => _bag[key] = value;
            }
        }

        [Fact]
        public void Indexer_IsSkipped_NotReportedAsAnUnmappedType()
        {
            // The fail-fast is about types the mapper does not cover *yet*. An indexer has no single
            // value to store and cannot even be read via GetValue(obj, null), so no mapping could ever
            // fix it — throwing here would reject the whole model for something unfixable.
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(IndexerModel));

            table.Fields.Values.Select(f => f.Property?.Name).Should().Contain(nameof(IndexerModel.Text));
            table.Fields.Values.Should().NotContain(f => f.Property!.GetIndexParameters().Length > 0);
        }

        [Table("NullableChar")]
        public class NullableCharModel : AbstractLogModel
        {
            public char? Flag { get; set; }
        }

        [Fact]
        public void NullableChar_NowReportsItselfInsteadOfVanishing()
        {
            // Not one of the types this task set out to map, and deliberately still unmapped: `char?`
            // fails the `PropertyType == typeof(char)` test and is not an enum. Recorded because the
            // fail-fast CHANGES its behaviour — it used to be dropped in silence, which is the same
            // data-loss shape as SH-H037 itself. A consumer with a `char?` column now finds out.
            var act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(NullableCharModel));

            act.Should().Throw<Birko.Data.Exceptions.FieldAttributeException>()
               .WithMessage($"*{nameof(NullableCharModel.Flag)}*");
        }

        [Table("OptedOut")]
        public class OptedOutModel : AbstractLogModel
        {
            [IgnoreField]
            public IEnumerable<string> ViaIgnoreField { get; set; } = null!;

            [System.ComponentModel.DataAnnotations.Schema.NotMapped]
            public IEnumerable<string> ViaNotMapped { get; set; } = null!;

            public string Text { get; set; } = null!;
        }

        [Fact]
        public void DeliberateOptOut_StillExcludesTheColumnWithoutThrowing()
        {
            // Fail-fast must fire on silence, not on an explicit instruction. Both escape hatches predate
            // this change and are honoured before the dispatch runs; without them the throw would be a
            // wall rather than a guard.
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(OptedOutModel));
            var mapped = table.Fields.Values.Select(f => f.Property?.Name).ToList();

            mapped.Should().NotContain(nameof(OptedOutModel.ViaIgnoreField));
            mapped.Should().NotContain(nameof(OptedOutModel.ViaNotMapped));
            mapped.Should().Contain(nameof(OptedOutModel.Text));
        }
    }
}
