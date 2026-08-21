using System.Data;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-257 — MySQL's indexed-string bound deliberately still reads the <b>narrow</b>
/// <c>AbstractField.IsIndexed</c>, not the wider <c>IsInIndexKey</c> that task added and MSSql adopted.
///
/// <para>
/// <b>Why this asymmetry exists.</b> <c>IsInIndexKey</c> is <c>IsIndexed || IsUnique || IsPrimary</c>, because
/// <c>FieldDefinition</c> emits <c>UNIQUE</c> and <c>PRIMARY KEY</c> as inline column constraints on all four
/// providers while <c>DataBase.LoadIndexes</c> marks only the columns named by <c>[IndexedField]</c> /
/// <c>[CompositeIndex]</c>. MySQL has the <b>identical hole</b> MSSql closed — a <c>[UniqueField]</c> on an
/// unlengthed string emits <c>LONGTEXT UNIQUE</c>, which is ERROR 1170 at <c>CREATE TABLE</c> — and switching
/// this connector to the wider property is a one-word change. It was not made, because it alters DDL on a
/// provider TASK-257 did not stand up and measure, and this epic's recurring defect is precisely a change
/// believed correct on a provider nobody ran.
/// </para>
/// <para>
/// <b>Why it is a test and not a comment.</b> A deliberate asymmetry is indistinguishable from an oversight
/// once the task that chose it is closed, and "I only wired the one provider" is construction, not evidence.
/// Without this pin the next reader unifies the two from symmetry and silently changes MySQL's emitted DDL.
/// When MySQL's half is measured and closed, <b>this file is what should fail</b> — update it then, with the
/// live 8.4 measurement in hand.
/// </para>
/// <para>No live server required; <c>ConvertType</c> is pure.</para>
/// </summary>
public class IndexKeyPredicateScopeTests
{
    private sealed class Holder
    {
        public string Text { get; set; } = null!;
    }

    private static MySQLConnector Connector()
        => new(new MySqlSettings("localhost", "db", "user", "pass"));

    private static StringField Unlengthed() =>
        new(typeof(Holder).GetProperty(nameof(Holder.Text))!, "Text");

    [Fact]
    public void An_indexed_unlengthed_string_is_bounded()
    {
        var field = Unlengthed();
        field.IsIndexed = true;

        Connector().ConvertType(DbType.String, field).Should().Be("VARCHAR(255)",
            "TASK-248: MySQL cannot index a BLOB/TEXT column without a key length (ERROR 1170)");
    }

    [Fact]
    public void An_unindexed_unlengthed_string_stays_longtext()
    {
        Connector().ConvertType(DbType.String, Unlengthed()).Should().Be("LONGTEXT",
            "only the columns an index names are bounded — the rest keep MySQL's unbounded type");
    }

    /// <summary>
    /// The pin. A UNIQUE or PRIMARY KEY column <i>is</i> an index key — <c>IsInIndexKey</c> says so — and
    /// MySQL still emits the unindexable type for it. That is the known, filed gap, not a passing case.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_unique_or_primary_unlengthed_string_is_NOT_yet_bounded_here(bool unique, bool primary)
    {
        var field = Unlengthed();
        field.IsUnique = unique;
        field.IsPrimary = primary;

        field.IsIndexed.Should().BeFalse("LoadIndexes marks only [IndexedField]/[CompositeIndex]");
        field.IsInIndexKey.Should().BeTrue(
            "a UNIQUE or PRIMARY KEY column is an index key — this is what MSSql now consults");

        Connector().ConvertType(DbType.String, field).Should().Be("LONGTEXT",
            "MySQL deliberately still reads the narrow IsIndexed. This assertion documents a KNOWN GAP: "
          + "`LONGTEXT UNIQUE` is ERROR 1170 at CREATE TABLE. Do not 'fix' this test by switching the "
          + "connector to IsInIndexKey from symmetry with MSSql — measure it on a live 8.4 first, then "
          + "change both the connector and this file together");
    }
}
