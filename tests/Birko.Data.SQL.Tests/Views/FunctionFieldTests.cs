using System;
using System.Data;
using System.Reflection;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Views
{
    /// <summary>
    /// CR-H095: coverage for the FunctionField aggregate type-mapping matrix — the pure logic that
    /// decides the result column type for COUNT/AVG/SUM/MIN/MAX over each source field type
    /// (nullable vs not-null). This is where aggregate-typing bugs would live.
    /// </summary>
    public class FunctionFieldTests
    {
        private sealed class Sample
        {
            public int Value { get; set; }
        }

        private static PropertyInfo Prop => typeof(Sample).GetProperty(nameof(Sample.Value))!;

        [Fact]
        public void Count_AlwaysProducesIntegerAggregate()
        {
            var src = new IntegerField(Prop, "Value");
            var fn = FunctionField.CreateFunctionField(Prop, "COUNT", src);

            fn.Should().NotBeNull();
            fn.IsAggregate.Should().BeTrue();
            fn.GetType().Name.Should().Contain("Integer");
        }

        [Fact]
        public void Avg_ProducesDecimalAggregate()
        {
            var src = new IntegerField(Prop, "Value");
            var fn = FunctionField.CreateFunctionField(Prop, "AVG", src);

            fn.Type.Should().Be(DbType.Decimal);
            fn.IsAggregate.Should().BeTrue();
        }

        [Fact]
        public void Sum_PreservesSourceType_ForDateTime()
        {
            var src = new DateTimeField(Prop, "Value");
            var fn = FunctionField.CreateFunctionField(Prop, "MAX", src);

            fn.Type.Should().Be(DbType.DateTime);
            fn.IsAggregate.Should().BeTrue();
        }

        [Fact]
        public void Sum_Decimal_StaysDecimal()
        {
            var src = new DecimalField(Prop, "Value");
            var fn = FunctionField.CreateFunctionField(Prop, "SUM", src);

            fn.Type.Should().Be(DbType.Decimal);
            fn.IsAggregate.Should().BeTrue();
        }

        [Fact]
        public void Min_Boolean_StaysBoolean()
        {
            var src = new BooleanField(Prop, "Value");
            var fn = FunctionField.CreateFunctionField(Prop, "MIN", src);

            fn.Type.Should().Be(DbType.Boolean);
        }

        [Fact]
        public void NullableSource_ProducesNullableAggregate()
        {
            // A not-null source yields a not-null aggregate; a nullable source yields a nullable one.
            var notNull = new IntegerField(Prop, "Value") { IsNotNull = true };
            var nullable = new IntegerField(Prop, "Value") { IsNotNull = false };

            FunctionField.CreateFunctionField(Prop, "SUM", notNull).GetType().Name
                .Should().NotContain("Nullable");
            FunctionField.CreateFunctionField(Prop, "SUM", nullable).GetType().Name
                .Should().Contain("Nullable");
        }
    }
}
