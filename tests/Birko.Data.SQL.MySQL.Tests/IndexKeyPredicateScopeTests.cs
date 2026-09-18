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
    /// <b>Inverted by TASK-265</b>, which is what this test was written to make happen. A UNIQUE or
    /// PRIMARY KEY column <i>is</i> an index key, and MySQL now bounds it like MSSql does.
    /// </summary>
    /// <remarks>
    /// It used to assert <c>LONGTEXT</c> and to say, in its own failure message, not to "fix" it by
    /// switching the connector from symmetry with MSSql — measure a live 8.4 first, then change the
    /// connector and this file together. That is exactly what happened: measured on 8.4.11,
    /// <c>LONGTEXT UNIQUE</c> and <c>LONGTEXT PRIMARY KEY</c> are both <c>ERROR 1170</c> at
    /// <c>CREATE TABLE</c>, and <c>VARCHAR(255)</c> accepts both.
    /// <para>
    /// <c>IsIndexed</c> is still false for these shapes — they carry inline constraints, which
    /// <c>LoadIndexes</c> does not resolve — so the assertion below is precisely the difference between the
    /// narrow flag and the wide one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_unique_or_primary_unlengthed_string_is_bounded_here_too(bool unique, bool primary)
    {
        var field = Unlengthed();
        field.IsUnique = unique;
        field.IsPrimary = primary;

        field.IsIndexed.Should().BeFalse("LoadIndexes marks only [IndexedField]/[CompositeIndex]");
        field.IsInIndexKey.Should().BeTrue(
            "a UNIQUE or PRIMARY KEY column is an index key — which is what both providers now consult");

        Connector().ConvertType(DbType.String, field).Should().Be("VARCHAR(255)",
            "TASK-265: LONGTEXT UNIQUE and LONGTEXT PRIMARY KEY are both ERROR 1170 at CREATE TABLE on "
          + "8.4.11, so such a table could not be created at all");
    }
}
