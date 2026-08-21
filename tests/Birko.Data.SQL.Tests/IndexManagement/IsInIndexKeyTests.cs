using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// TASK-257 — <c>AbstractField.IsInIndexKey</c>: does any declared index, <c>UNIQUE</c> or
    /// <c>PRIMARY KEY</c> name this column?
    ///
    /// <para>
    /// It exists because <c>IsIndexed</c> (TASK-248) answers a narrower question than its readers need.
    /// <c>DataBase.LoadIndexes</c> marks only the columns named by <c>[IndexedField]</c> and
    /// <c>[CompositeIndex]</c>, while every provider's <c>FieldDefinition</c> emits <c>UNIQUE</c> and
    /// <c>PRIMARY KEY</c> as <b>inline column constraints</b> — which are index keys too. A provider whose
    /// key types are restricted (SQL Server refuses a MAX type as a key; MySQL refuses BLOB/TEXT without a
    /// key length) must therefore consult the wider property, or it bounds the declared-index case and
    /// leaves the constraint case broken.
    /// </para>
    /// <para>
    /// These tests live here, in the project that <i>declares</i> the property, rather than only in the
    /// provider suites that read it — per § Testing, new public surface is tested in its own
    /// <c>Birko.{Project}.Tests</c>. The provider-specific consequences (which column type each emits) are
    /// asserted in <c>Birko.Data.SQL.MSSql.Tests</c> and <c>Birko.Data.SQL.MySQL.Tests</c>.
    /// </para>
    /// </summary>
    public class IsInIndexKeyTests
    {
        public abstract class TenantBase : AbstractLogModel
        {
            public Guid TenantGuid { get; set; }
        }

        [Table("IdxKeyPerProp")]
        public class PerPropertyEntity : AbstractLogModel
        {
            [IndexedField("ix_idxkeyperprop_code")]
            public string Code { get; set; } = null!;

            public string Plain { get; set; } = null!;
        }

        [Table("IdxKeyComposite")]
        [CompositeIndex("ux_idxkeycomposite", nameof(TenantGuid), nameof(Number), IsUnique = true)]
        public class CompositeEntity : TenantBase
        {
            public string Number { get; set; } = null!;

            public string Plain { get; set; } = null!;
        }

        [Table("IdxKeyConstraints")]
        public class ConstraintEntity : AbstractLogModel
        {
            [UniqueField]
            public string Sku { get; set; } = null!;

            [PrimaryField]
            public string NaturalKey { get; set; } = null!;
        }

        private static AbstractField Field(Type entity, string property)
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(entity);
            var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == property);
            field.Should().NotBeNull($"'{property}' must map to a column");
            return field!;
        }

        // ---- the union itself, on a bare field

        [Fact]
        public void A_field_with_nothing_set_is_not_an_index_key()
        {
            var field = new StringField(
                typeof(PerPropertyEntity).GetProperty(nameof(PerPropertyEntity.Plain))!, "Plain");

            field.IsInIndexKey.Should().BeFalse();
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void Any_one_of_the_three_flags_makes_it_an_index_key(bool indexed, bool unique, bool primary)
        {
            var field = new StringField(
                typeof(PerPropertyEntity).GetProperty(nameof(PerPropertyEntity.Plain))!, "Plain")
            {
                IsIndexed = indexed,
                IsUnique = unique,
                IsPrimary = primary,
            };

            field.IsInIndexKey.Should().BeTrue();
        }

        [Fact]
        public void It_tracks_its_inputs_rather_than_latching()
        {
            var field = new StringField(
                typeof(PerPropertyEntity).GetProperty(nameof(PerPropertyEntity.Plain))!, "Plain");

            field.IsUnique = true;
            field.IsInIndexKey.Should().BeTrue();

            // A computed property, not a flag something has to remember to clear. LoadIndexes mutates
            // IsIndexed after LoadFields has run, so a cached value would be stale by construction.
            field.IsUnique = false;
            field.IsInIndexKey.Should().BeFalse();
        }

        // ---- through the real attribute pipeline, which is where IsIndexed is actually set

        [Fact]
        public void A_per_property_indexed_column_is_an_index_key()
        {
            var field = Field(typeof(PerPropertyEntity), nameof(PerPropertyEntity.Code));

            field.IsIndexed.Should().BeTrue("[IndexedField] is one of LoadIndexes' two marking sites");
            field.IsInIndexKey.Should().BeTrue();
        }

        [Fact]
        public void A_composite_indexed_column_is_an_index_key()
        {
            var field = Field(typeof(CompositeEntity), nameof(CompositeEntity.Number));

            field.IsIndexed.Should().BeTrue("[CompositeIndex] is the other marking site");
            field.IsInIndexKey.Should().BeTrue();
        }

        [Theory]
        [InlineData(nameof(PerPropertyEntity.Plain))]
        public void A_column_no_index_names_is_not_an_index_key(string property)
        {
            var field = Field(typeof(PerPropertyEntity), property);

            field.IsIndexed.Should().BeFalse();
            field.IsInIndexKey.Should().BeFalse(
                "the property must not over-report, or a provider bounds columns that need no bound");
        }

        /// <summary>
        /// The gap this property was added to close: <c>LoadIndexes</c> never marks a <c>UNIQUE</c> or
        /// <c>PRIMARY KEY</c> column, so <c>IsIndexed</c> alone cannot see an index key that
        /// <c>FieldDefinition</c> is about to emit inline.
        /// </summary>
        [Theory]
        [InlineData(nameof(ConstraintEntity.Sku))]
        [InlineData(nameof(ConstraintEntity.NaturalKey))]
        public void A_unique_or_primary_column_is_an_index_key_that_IsIndexed_cannot_see(string property)
        {
            var field = Field(typeof(ConstraintEntity), property);

            field.IsIndexed.Should().BeFalse(
                "this is the whole reason IsInIndexKey exists — LoadIndexes resolves only the two index "
              + "attributes, so the inline-constraint case is invisible to IsIndexed");
            field.IsInIndexKey.Should().BeTrue();
        }
    }
}
