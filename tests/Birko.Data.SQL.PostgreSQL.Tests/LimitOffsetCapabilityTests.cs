using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-278 — the paging capability's <b>false</b> side on PostgreSQL.
/// </summary>
/// <remarks>
/// SQL Server needs a synthesised <c>ORDER BY</c> for a limited read because <c>OFFSET</c>/<c>FETCH</c> is
/// part of its sort clause; PostgreSQL takes a bare <c>LIMIT</c>/<c>OFFSET</c> and must not gain one.
/// Asserting the false side is what stops the flag being indistinguishable from an unconditional true —
/// and it needs no server, so it runs everywhere.
/// </remarks>
public class LimitOffsetCapabilityTests
{
    [Fact]
    public void RequiresOrderByForPaging_is_false_on_postgresql()
    {
        new PostgreSQLConnector(new PostgreSqlSettings("localhost", "db", "u", "p")).RequiresOrderByForPaging.Should().BeFalse();
    }
}
