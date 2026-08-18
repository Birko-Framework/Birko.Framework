using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-248 — SQL Server is <b>unaffected</b> by the indexed-string fix, asserted rather than assumed.
///
/// <para>
/// MySQL cannot index a BLOB/TEXT column without a key length (ERROR 1170), so its connector now emits
/// <c>VARCHAR(255)</c> for a string the schema declares an index over. SQL Server has no such restriction on
/// the type this connector emits, so <c>IsIndexed</c> must leave it alone. Scoping the change to MySQL is what
/// keeps three working providers working — seven live consumer entities declare UNIQUE composites over
/// unbounded strings.
/// </para>
/// <para>
/// Note this connector maps an unbounded string to the deprecated <c>TEXT</c> type, which SQL Server itself
/// cannot index either — a pre-existing divergence from the MySQL problem, out of scope here and not silently
/// "fixed" by this task. Pinned so the next reader sees it is known rather than overlooked.
/// </para>
/// </summary>
public class IndexedStringColumnTypeTests
{
    private sealed class Holder
    {
        public string Text { get; set; } = null!;
    }

    private static MSSqlConnector Connector() => new(new MSSqlSettings("localhost", "db"));

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
                "the indexed-string bound is MySQL-only; this provider's mapping is unchanged by TASK-248");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_explicit_length_still_produces_nvarchar(bool indexed)
    {
        var bounded = new CharField(typeof(Holder).GetProperty(nameof(Holder.Text))!, "Text", lenght: 64)
        {
            IsIndexed = indexed
        };

        Connector().ConvertType(DbType.String, bounded).Should().Be("NVARCHAR(64)");
    }
}
