using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-278 — the paging capability's <b>false</b> side on MySQL.
/// </summary>
/// <remarks>
/// SQL Server needs a synthesised <c>ORDER BY</c> for a limited read because <c>OFFSET</c>/<c>FETCH</c> is
/// part of its sort clause; MySQL takes a bare <c>LIMIT</c>/<c>OFFSET</c> and must not gain one.
/// Asserting the false side is what stops the flag being indistinguishable from an unconditional true —
/// and it needs no server, so it runs everywhere.
/// </remarks>
public class LimitOffsetCapabilityTests
{
    [Fact]
    public void RequiresOrderByForPaging_is_false_on_mysql()
    {
        new MySQLConnector(new MySqlSettings("localhost", "db", "u", "p")).RequiresOrderByForPaging.Should().BeFalse();
    }
}
