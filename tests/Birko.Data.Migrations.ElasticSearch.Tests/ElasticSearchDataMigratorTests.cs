using System;
using Birko.Data.Migrations.ElasticSearch.Context;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// CR-L143: range-operator values are validated before building a NumericRangeQuery — a null/non-numeric
/// value threw nothing (Convert.ToDouble(null) == 0) and silently produced a wrong range. CR-L144: CopyData
/// rejects a transformJson instead of silently ignoring it (the ES reindex path applies no transform).
/// </summary>
public class ElasticSearchDataMigratorTests
{
    // ---- L143: ToRangeBound --------------------------------------------------

    [Fact]
    public void ToRangeBound_null_throws_ArgumentException()
    {
        Action act = () => ElasticSearchDataMigrator.ToRangeBound(null, "age", "$gt");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToRangeBound_non_numeric_string_throws_ArgumentException()
    {
        Action act = () => ElasticSearchDataMigrator.ToRangeBound("abc", "age", "$gte");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(18L, 18.0)]
    [InlineData("21", 21.0)]
    [InlineData(3.5, 3.5)]
    public void ToRangeBound_numeric_values_convert(object value, double expected)
    {
        ElasticSearchDataMigrator.ToRangeBound(value, "age", "$lt").Should().Be(expected);
    }

    // ---- L144: CopyData transform rejection ----------------------------------

    [Fact]
    public void CopyData_with_a_transform_throws_NotSupported()
    {
        // Offline ElasticClient — never connects because the guard throws before any request.
        var migrator = new ElasticSearchDataMigrator(new ElasticClient());

        Action act = () => migrator.CopyData("src", "dst", "{\"$set\":{\"x\":1}}");

        act.Should().Throw<NotSupportedException>();
    }
}
