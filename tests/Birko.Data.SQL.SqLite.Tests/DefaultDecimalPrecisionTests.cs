using Birko.Data.SQL.Connectors;
using Birko.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-512 — the precision/scale the SQL layer gives an unprecisioned <c>decimal</c> is the framework's
/// canonical pair, the one every <c>Birko.Models.*.SQL</c> mapping declares explicitly. Pinned here because
/// <c>Birko.Data.SQL</c> cannot reference <c>Birko.Models</c> (dependency direction), so the two constants are
/// spelled twice and only a test keeps them one value. Lives in this project because it is the one that
/// compiles both.
/// </summary>
public class DefaultDecimalPrecisionTests
{
    [Fact]
    public void The_SQL_default_is_the_models_canonical_pair()
    {
        AbstractConnectorBase.DefaultDecimalPrecision.Should().Be(ValueData.StoreDecimalPrecision);
        AbstractConnectorBase.DefaultDecimalScale.Should().Be(ValueData.StoreDecimalPlaces);
    }
}
