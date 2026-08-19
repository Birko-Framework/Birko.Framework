using System;
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
    /// TASK-263 — <c>[UtcField]</c> switches a <c>DateTime</c> property from a <b>wall clock</b> to an
    /// <b>instant</b>.
    ///
    /// <para>
    /// TASK-256 recorded that a plain Birko <c>DateTime</c> column stores the value's date and time components
    /// as supplied, with <c>Kind</c> not persisted — correct for a local calendar date, but unable to say which
    /// instant a timestamp names. Its rule named an opt-in for the other meaning, and that opt-in did not
    /// exist: <c>ConvertType</c> mapped <c>DbType.DateTimeOffset</c> to <c>TIMESTAMPTZ</c> (and
    /// <c>DATETIMEOFFSET</c> on MSSql), but <c>CreateAbstractField</c> had no arm that could reach it and no
    /// attribute could override a field's <c>DbType</c>. This is that opt-in.
    /// </para>
    ///
    /// <para>
    /// <b>Every test reaches its field through <c>DataBase.LoadTable</c>, never by constructing a
    /// <c>UtcDateTimeField</c> directly.</b> Not stylistic: SH-H037 shipped a suite that built its fields by
    /// hand and stayed green with the dispatch fix reverted, and TASK-221 shipped a rewriter that was tested
    /// and never called. Going through <c>LoadTable</c> is what makes these witness the <i>mapping</i> rather
    /// than just the class.
    /// </para>
    ///
    /// <para>
    /// The storage half — that the instant is exact on a real server, that the column really is the
    /// timezone-aware type, and what the fallback providers do — lives in the four provider suites, because
    /// only a live server can answer it.
    /// </para>
    /// </summary>
    public class UtcFieldMappingTests
    {
        [Table("UtcFieldSpread")]
        public class StampModel : AbstractLogModel
        {
            /// <summary>An instant.</summary>
            [UtcField]
            public DateTime ObservedAt { get; set; }

            /// <summary>An instant, optional.</summary>
            [UtcField]
            public DateTime? AcknowledgedAt { get; set; }

            /// <summary>A wall clock — TASK-256's rule, unchanged, on the same entity.</summary>
            public DateTime NoticeDate { get; set; }
        }

        [Table("UtcFieldOnAString")]
        public class BadStringModel : AbstractLogModel
        {
            [UtcField]
            public string When { get; set; } = null!;
        }

        [Table("UtcFieldOnAnInt")]
        public class BadIntModel : AbstractLogModel
        {
            [UtcField]
            public int When { get; set; }
        }

        private static AbstractField FieldFor(Type modelType, string propertyName)
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(modelType);
            var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == propertyName);
            field.Should().NotBeNull($"'{propertyName}' must map to a column");
            return field!;
        }

        // ============================================================ the mapping

        [Fact]
        public void A_utc_marked_datetime_maps_to_the_timezone_aware_dbtype()
        {
            var field = FieldFor(typeof(StampModel), nameof(StampModel.ObservedAt));

            field.Should().BeOfType<UtcDateTimeField>();
            field.Type.Should().Be(DbType.DateTimeOffset,
                "this is the DbType every connector already maps to its timezone-aware column type — "
              + "TIMESTAMPTZ on PostgreSQL, DATETIMEOFFSET on MSSql");
        }

        [Fact]
        public void A_nullable_utc_marked_datetime_maps_to_the_nullable_field()
        {
            var field = FieldFor(typeof(StampModel), nameof(StampModel.AcknowledgedAt));

            field.Should().BeOfType<NullableUtcDateTimeField>();
            field.Type.Should().Be(DbType.DateTimeOffset);
            field.IsNotNull.Should().BeFalse();
        }

        /// <summary>
        /// The two rules coexist per property (criterion 6, mapping half). This also replaces TASK-256's
        /// one-way premise test: that task's fix strips Kind from every bound DateTime on PostgreSQL, safe only
        /// while no DateTime could target a timestamptz column. That premise is now false, and this asserts the
        /// two-way rule it became.
        /// </summary>
        [Fact]
        public void An_unmarked_datetime_on_the_same_entity_is_still_a_wall_clock()
        {
            var utc = FieldFor(typeof(StampModel), nameof(StampModel.ObservedAt));
            var wall = FieldFor(typeof(StampModel), nameof(StampModel.NoticeDate));

            wall.Should().BeOfType<DateTimeField>();
            wall.Type.Should().Be(DbType.DateTime,
                "TASK-256's rule is unchanged for an unmarked property — the opt-in is per property, so one "
              + "entity can carry both meanings");
            utc.Type.Should().NotBe(wall.Type,
                "if these ever agree, one of the two rules has silently absorbed the other");
        }

        // ============================================================ the bound value

        /// <summary>
        /// <c>Write</c> returns a <see cref="DateTimeOffset"/>, and that is load-bearing rather than
        /// incidental. <c>PostgreSQLConnector.NormalizeTimestampValue</c> (TASK-256) strips <c>Kind</c> from
        /// every bound <c>DateTime</c>; a <c>Kind=Utc</c> value stripped to <c>Unspecified</c> is inferred as
        /// <c>timestamp</c>, which PostgreSQL then re-reads in the session's time zone when assigning it to a
        /// <c>timestamptz</c> column — a different instant, silently. Binding a <c>DateTimeOffset</c> keeps this
        /// value outside that helper's <c>is DateTime</c> test, which is what lets the two features compose
        /// without either acquiring per-column knowledge it has no way to obtain.
        /// </summary>
        [Fact]
        public void Write_binds_a_DateTimeOffset_so_the_postgres_kind_stripper_cannot_touch_it()
        {
            var field = FieldFor(typeof(StampModel), nameof(StampModel.ObservedAt));
            var model = new StampModel { ObservedAt = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc) };

            var bound = field.Write(model);

            bound.Should().BeOfType<DateTimeOffset>(
                "a DateTime here would be caught by NormalizeTimestampValue and stripped to Unspecified, "
              + "making the stored instant depend on the server's session time zone");
        }

        [Theory]
        [InlineData(DateTimeKind.Utc)]
        [InlineData(DateTimeKind.Unspecified)]
        [InlineData(DateTimeKind.Local)]
        public void Write_normalises_every_kind_to_the_same_definite_instant(DateTimeKind kind)
        {
            var field = FieldFor(typeof(StampModel), nameof(StampModel.ObservedAt));
            var wall = new DateTime(2026, 3, 15, 10, 30, 0);
            var model = new StampModel { ObservedAt = DateTime.SpecifyKind(wall, kind) };

            var bound = field.Write(model).Should().BeOfType<DateTimeOffset>().Subject;

            var expected = kind == DateTimeKind.Local
                ? DateTime.SpecifyKind(wall, DateTimeKind.Local).ToUniversalTime()
                : wall;
            bound.UtcDateTime.Should().Be(expected,
                kind == DateTimeKind.Unspecified
                    ? "an Unspecified value is taken AS UTC, because [UtcField] declares the property holds "
                    + "UTC — reading it as local would make the stored instant depend on the machine the "
                    + "write ran on"
                    : $"Kind={kind} must resolve to a definite instant");
            bound.Offset.Should().Be(TimeSpan.Zero,
                "the offset is normalised on every provider, deliberately — see the UtcField attribute");
        }

        [Fact]
        public void Write_passes_a_null_through_untouched()
        {
            var field = FieldFor(typeof(StampModel), nameof(StampModel.AcknowledgedAt));
            var model = new StampModel { AcknowledgedAt = null };

            field.Write(model).Should().BeNull("a null optional instant is still null, not the epoch");
        }

        // ============================================================ the guard

        [Theory]
        [InlineData(typeof(BadStringModel), "System.String")]
        [InlineData(typeof(BadIntModel), "System.Int32")]
        public void Utc_field_on_a_non_datetime_property_is_refused_at_table_load(Type modelType, string clrType)
        {
            var act = () => Birko.Data.SQL.DataBase.LoadTable(modelType);

            var ex = act.Should().Throw<Birko.Data.Exceptions.FieldAttributeException>(
                "silently ignoring the attribute would leave the model declaring an instant while the column "
              + "stored whatever that type maps to, with nothing to notice — the SH-H037 shape").Which;
            ex.Message.Should().Contain("When", "the message must name the offending property");
            ex.Message.Should().Contain(clrType, "and its CLR type, so the fix is obvious from the message");
            ex.Message.Should().Contain("DateTime", "and say what the attribute is actually for");
        }
    }
}
