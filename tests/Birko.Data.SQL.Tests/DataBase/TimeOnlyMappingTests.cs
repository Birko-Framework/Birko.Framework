using System;
using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    /// <summary>
    /// SH-H038 — <c>TimeOnly</c> was the one BCL value type <c>CreateAbstractField</c> had no arm for.
    /// While an unmapped type was silently skipped (SH-H037) that only lost the column; once the
    /// fallthrough started throwing, a single <c>TimeOnly</c> property took down <b>every</b> route on the
    /// owning entity, because the throw is raised at TABLE LOAD rather than on the query that touches the
    /// column. That is the predicted blast radius of SH-H037's fail-fast arriving in a consumer
    /// (Symbio TASK-361).
    /// <para>
    /// Every test here reaches its field through <c>DataBase.LoadTable</c> rather than constructing a
    /// <c>TimeOnlyField</c> by hand. That is deliberate: SH-H037 shipped a suite that built its fields
    /// directly and stayed green with the dispatch fix reverted. Going through <c>LoadTable</c> is what
    /// makes these witness the mapping rather than just the class.
    /// </para>
    /// </summary>
    public class TimeOnlyMappingTests
    {
        [Table("TimeOnlySpread")]
        public class OpeningHoursModel : AbstractLogModel
        {
            public TimeOnly OpensAt { get; set; }
            public TimeOnly? ClosesAt { get; set; }
            public string Text { get; set; } = null!;
        }

        private static AbstractField FieldFor(string propertyName)
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(OpeningHoursModel));
            var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == propertyName);
            field.Should().NotBeNull($"'{propertyName}' must map to a column");
            return field!;
        }

        // ---- the dispatch itself ----------------------------------------------------------------------

        [Fact]
        public void TimeOnlyProperty_NoLongerTakesTheWholeTableDownAtLoad()
        {
            // The whole point of the finding: before the arm existed this call threw, so an entity with a
            // schedule boundary on it could not be read, written, listed or deleted at all.
            var act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(OpeningHoursModel));

            act.Should().NotThrow();
        }

        [Theory]
        [InlineData(nameof(OpeningHoursModel.OpensAt), typeof(TimeOnlyField), true)]
        [InlineData(nameof(OpeningHoursModel.ClosesAt), typeof(NullableTimeOnlyField), false)]
        public void TimeOnly_MapsToItsOwnFieldAndNullability(
            string propertyName, Type expectedFieldType, bool expectedNotNull)
        {
            var field = FieldFor(propertyName);

            field.Should().BeOfType(expectedFieldType);
            field.IsNotNull.Should().Be(expectedNotNull);
        }

        [Fact]
        public void TimeOnly_IsStoredAsText_NotAsADbTypeTimeThatCarriesADate()
        {
            // AbstractConnectorBase maps DbType.Time to typeof(DateTime), so the value would round-trip
            // through a type carrying a date component TimeOnly does not have — and the dialects disagree
            // about what a bare TIME column is (SQLite has no time type at all). DbType.String renders as
            // TEXT/VARCHAR everywhere with no per-dialect special case.
            FieldFor(nameof(OpeningHoursModel.OpensAt)).Type.Should().Be(System.Data.DbType.String);
            FieldFor(nameof(OpeningHoursModel.ClosesAt)).Type.Should().Be(System.Data.DbType.String);
        }

        [Fact]
        public void TimeOnly_IsNotRoutedThroughDateTime()
        {
            // A DateTimeField would attach today's date to a wall-clock boundary, so "active from 08:00"
            // would stop matching tomorrow.
            FieldFor(nameof(OpeningHoursModel.OpensAt)).Should().NotBeAssignableTo<DateTimeField>();
        }

        // ---- the stored shape: fixed width, or range queries lie ---------------------------------------

        [Theory]
        [InlineData(9, 5, 0, "09:05:00")]
        [InlineData(0, 0, 0, "00:00:00")]
        [InlineData(23, 59, 59, "23:59:59")]
        [InlineData(8, 0, 0, "08:00:00")]
        public void Write_ZeroPadsEveryComponent(int hour, int minute, int second, string expected)
        {
            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var model = new OpeningHoursModel { OpensAt = new TimeOnly(hour, minute, second) };

            field.Write(model).Should().Be(expected);
        }

        [Fact]
        public void Write_ProducesTextWhoseLexicalOrderIsChronologicalOrder()
        {
            // This is why the width is fixed rather than a cosmetic choice. Text columns compare
            // lexically, so `<`, `>` and BETWEEN on a time column are only honest while every value is the
            // same length: unpadded, "9:05" sorts AFTER "10:00". Same class of defect as TASK-355, where a
            // 10-character date string was compared against a full timestamp and the shorter prefix won.
            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var chronological = new[]
            {
                new TimeOnly(0, 0, 0),
                new TimeOnly(8, 0, 0),
                new TimeOnly(9, 5, 0),
                new TimeOnly(10, 0, 0),
                new TimeOnly(23, 59, 59),
            };

            var stored = chronological
                .Select(t => (string)field.Write(new OpeningHoursModel { OpensAt = t })!)
                .ToList();

            stored.Should().BeInAscendingOrder(StringComparer.Ordinal);
            stored.Should().OnlyContain(s => s.Length == 8);
        }

        [Fact]
        public void Write_IsIndependentOfTheServerLocale()
        {
            // In a custom date/time format `:` means "the culture's time separator", which is not `:`
            // everywhere. A locale-dependent column would be unreadable by a differently configured
            // replica, so the separator is escaped AND the format is applied under InvariantCulture.
            var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            hostile.DateTimeFormat.TimeSeparator = ".";

            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var model = new OpeningHoursModel { OpensAt = new TimeOnly(9, 5, 0) };

            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = hostile;
                field.Write(model).Should().Be("09:05:00");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Write_OnANullNullableTime_StoresNull()
        {
            var field = FieldFor(nameof(OpeningHoursModel.ClosesAt));

            field.Write(new OpeningHoursModel { ClosesAt = null }).Should().BeNull();
        }

        // ---- reading back -----------------------------------------------------------------------------

        [Fact]
        public void Read_RoundTripsTheCanonicalShape()
        {
            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var model = new OpeningHoursModel();

            field.Read(model, new SingleValueReader("08:30:00"), 0);

            model.OpensAt.Should().Be(new TimeOnly(8, 30, 0));
        }

        [Theory]
        [InlineData("08:30:00")]            // canonical
        [InlineData("08:30")]               // written before this mapping existed, or by another tool
        public void Read_AcceptsTheLenientInvariantShapesToo(string stored)
        {
            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var model = new OpeningHoursModel();

            field.Read(model, new SingleValueReader(stored), 0);

            model.OpensAt.Should().Be(new TimeOnly(8, 30, 0));
        }

        [Fact]
        public void Read_AcceptsADriverThatMaterialisesTheColumnAsATimeSpan()
        {
            // A provider with a native TIME type hands back TimeSpan, not text. Refusing it would make the
            // column unreadable on exactly the dialects that model it best.
            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var model = new OpeningHoursModel();

            field.Read(model, new SingleValueReader(new TimeSpan(8, 30, 0)), 0);

            model.OpensAt.Should().Be(new TimeOnly(8, 30, 0));
        }

        [Fact]
        public void Read_NonNullableArm_FallsBackToMidnightRatherThanKillingTheRead()
        {
            // The property cannot hold null. Throwing here would take down every read of the table for one
            // bad cell — precisely the failure this field exists to remove.
            var field = FieldFor(nameof(OpeningHoursModel.OpensAt));
            var model = new OpeningHoursModel { OpensAt = new TimeOnly(8, 0, 0) };

            field.Read(model, new SingleValueReader("not a time"), 0);

            model.OpensAt.Should().Be(default(TimeOnly));
        }

        [Fact]
        public void Read_NullableArm_ReportsNullRatherThanMidnight()
        {
            // The distinction that makes the nullable arm worth having: `null` says "no boundary set",
            // midnight is a real time that a range query would match.
            var field = FieldFor(nameof(OpeningHoursModel.ClosesAt));
            var model = new OpeningHoursModel { ClosesAt = new TimeOnly(17, 0, 0) };

            field.Read(model, new SingleValueReader(DBNull.Value), 0);

            model.ClosesAt.Should().BeNull();
        }

        [Fact]
        public void Read_NullableArm_DoesNotTurnAnUnparseableValueIntoMidnight()
        {
            var field = FieldFor(nameof(OpeningHoursModel.ClosesAt));
            var model = new OpeningHoursModel { ClosesAt = new TimeOnly(17, 0, 0) };

            field.Read(model, new SingleValueReader("not a time"), 0);

            model.ClosesAt.Should().BeNull();
        }

        /// <summary>
        /// Minimal <see cref="DbDataReader"/> over a single cell. Only the members the field arms actually
        /// call are implemented; everything else throws so a future test cannot lean on a silent default.
        /// </summary>
        private sealed class SingleValueReader : DbDataReader
        {
            private readonly object _value;

            public SingleValueReader(object value) => _value = value;

            public override object GetValue(int ordinal) => _value;
            public override bool IsDBNull(int ordinal) => _value is DBNull;
            public override int FieldCount => 1;

            public override object this[int ordinal] => GetValue(ordinal);
            public override object this[string name] => GetValue(0);
            public override int Depth => 0;
            public override bool HasRows => true;
            public override bool IsClosed => false;
            public override int RecordsAffected => 0;

            public override bool GetBoolean(int ordinal) => throw new NotImplementedException();
            public override byte GetByte(int ordinal) => throw new NotImplementedException();
            public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
                => throw new NotImplementedException();
            public override char GetChar(int ordinal) => throw new NotImplementedException();
            public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
                => throw new NotImplementedException();
            public override string GetDataTypeName(int ordinal) => throw new NotImplementedException();
            public override DateTime GetDateTime(int ordinal) => throw new NotImplementedException();
            public override decimal GetDecimal(int ordinal) => throw new NotImplementedException();
            public override double GetDouble(int ordinal) => throw new NotImplementedException();
            public override Type GetFieldType(int ordinal) => _value.GetType();
            public override float GetFloat(int ordinal) => throw new NotImplementedException();
            public override Guid GetGuid(int ordinal) => throw new NotImplementedException();
            public override short GetInt16(int ordinal) => throw new NotImplementedException();
            public override int GetInt32(int ordinal) => throw new NotImplementedException();
            public override long GetInt64(int ordinal) => throw new NotImplementedException();
            public override string GetName(int ordinal) => throw new NotImplementedException();
            public override int GetOrdinal(string name) => throw new NotImplementedException();
            public override string GetString(int ordinal) => throw new NotImplementedException();
            public override int GetValues(object[] values) => throw new NotImplementedException();
            public override bool NextResult() => false;
            public override bool Read() => false;
            public override IEnumerator GetEnumerator() => throw new NotImplementedException();
        }
    }
}
