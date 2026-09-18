using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-248 — PostgreSQL is <b>unaffected</b> by the indexed-string fix, and this is the assertion of it.
///
/// <para>
/// MySQL cannot index a BLOB/TEXT column without a key length (ERROR 1170), so its connector now emits
/// <c>VARCHAR(255)</c> for a string the schema declares an index over. **PostgreSQL must not follow.** It
/// indexes <c>text</c> natively, and this is the provider where the seven live consumer entities — Symbio's
/// docnumber and e-mail UNIQUE composites over plain <c>string</c> properties — actually run. Bounding the
/// column here would impose a 255-character ceiling on columns that have none today, so a longer value that
/// writes fine now would start failing: breaking a working provider to fix a broken one.
/// </para>
/// </summary>
public class IndexedStringColumnTypeTests
{
    private sealed class Holder
    {
        public string Text { get; set; } = null!;
    }

    private static PostgreSQLConnector Connector() =>
        new(new PostgreSqlSettings("localhost", "db", "user", "pass"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unbounded_string_maps_to_text_whether_indexed_or_not(bool indexed)
    {
        var field = new StringField(typeof(Holder).GetProperty(nameof(Holder.Text))!, "Text")
        {
            IsIndexed = indexed
        };

        Connector().ConvertType(DbType.String, field)
            .Should().Be("TEXT",
                "PostgreSQL indexes text natively, so IsIndexed must not narrow the column — the 7 live "
              + "consumer entities with UNIQUE composites over unbounded strings run on this provider");
    }

    /// <summary>An explicit <c>[MaxLengthField]</c> still produces VARCHAR, unchanged by the new flag.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_explicit_length_still_produces_varchar(bool indexed)
    {
        var bounded = new CharField(typeof(Holder).GetProperty(nameof(Holder.Text))!, "Text", lenght: 64)
        {
            IsIndexed = indexed
        };

        Connector().ConvertType(DbType.String, bounded).Should().Be("VARCHAR(64)");
    }
}
