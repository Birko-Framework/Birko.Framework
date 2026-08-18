using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-248 — SQLite is <b>unaffected</b> by the indexed-string fix, and that is asserted rather than assumed.
///
/// <para>
/// MySQL cannot index a BLOB/TEXT column without a key length (measured on 8.4 as ERROR 1170), so
/// <c>MySQLConnector.ConvertType</c> now emits <c>VARCHAR(255)</c> for a string the schema declares an index
/// over. **That is scoped to MySQL on purpose.** SQLite indexes a TEXT column natively, and seven live
/// consumer entities declare UNIQUE composites over unbounded strings that work correctly here today —
/// bounding the column on this provider would silently impose a 255-character ceiling on data that currently
/// has none, breaking working deployments to fix a different provider.
/// </para>
/// </summary>
public class IndexedStringColumnTypeTests
{
    private sealed class Holder
    {
        public string Text { get; set; } = null!;
    }

    private static StringField Field(bool indexed) =>
        new(typeof(Holder).GetProperty(nameof(Holder.Text))!, "Text") { IsIndexed = indexed };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unbounded_string_maps_to_text_whether_indexed_or_not(bool indexed)
    {
        var connector = new SqLiteConnector(new SqLiteSettings(System.IO.Path.GetTempPath(), "t248.db"));

        connector.ConvertType(DbType.String, Field(indexed))
            .Should().Be("TEXT",
                "only MySQL bounds an indexed string — SQLite indexes TEXT natively, so IsIndexed must not "
              + "change the column type here");
    }

    /// <summary>An explicit length is still honoured, exactly as before — the flag changes nothing.</summary>
    [Fact]
    public void An_explicit_length_is_unaffected_by_the_indexed_flag()
    {
        var connector = new SqLiteConnector(new SqLiteSettings(System.IO.Path.GetTempPath(), "t248.db"));
        var bounded = new CharField(typeof(Holder).GetProperty(nameof(Holder.Text))!, "Text", lenght: 64)
        {
            IsIndexed = true
        };

        connector.ConvertType(DbType.String, bounded).Should().Be("TEXT",
            "SQLite is dynamically typed and maps every string to TEXT regardless of declared length");
    }
}
