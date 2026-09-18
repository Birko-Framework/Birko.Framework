using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tests.TestHelpers;
using Birko.Data.SQL.Tests.TestResources.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Birko.Data.SQL.Tests.Connectors
{
    /// <summary>
    /// Enum values bound into a SQL command must be converted to their underlying integral type. Enums
    /// persist as INTEGER (<c>AbstractField.CreateField</c> → <c>IntegerField</c>), so leaving a boxed enum
    /// for the provider to interpret makes the comparison provider-dependent — Microsoft.Data.Sqlite
    /// converts it, Npgsql rejects an unmapped CLR enum. Assertions are bidirectional on purpose: the
    /// bound value must BE the integer AND must NOT still be the enum.
    /// <para>
    /// Enum EQUALITY was never affected — the C# compiler lifts <c>x.State == Foo</c> to the underlying
    /// integral type inside the expression tree, so the constant arrives as an <c>int</c>. A collection in
    /// <c>set.Contains(x.State)</c> carries no such conversion.
    /// </para>
    /// The zero-rows defect that prompted this (Symbio TASK-249/TASK-254) was a *parser* bug — a trailing
    /// <c>IEqualityComparer</c> argument turning the condition into <c>IS NULL</c>, covered by
    /// <see cref="ContainsOverEnumCollection_TranslatesToInCondition"/> and, end-to-end against a real
    /// SQLite file, by <c>Birko.Data.SQL.SqLite.Tests.SqlEnumInPredicateTests</c>.
    /// </summary>
    public class EnumParameterBindingTests
    {
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly TestDbCommand _command;

        public EnumParameterBindingTests()
        {
            _connectorMock = new Mock<AbstractConnector>(new Birko.Configuration.PasswordSettings());
            _command = new TestDbCommand();
            _connectorMock.CallBase = true;
        }

        [Fact]
        public void InCondition_WithEnumValues_BindsUnderlyingIntegers()
        {
            var condition = Condition.Create(
                "State",
                new object[] { EnumModelState.Paid, EnumModelState.Shipped },
                ConditionType.In);

            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);

            sql.Should().Be("State IN (@WHEREState0_0, @WHEREState1_1)");
            var values = _command.TestParameters.All.Select(p => p.Value).ToList();
            values.Should().Equal(new object[] { 2, 4 });
            values.Should().NotContain(EnumModelState.Paid, "a boxed enum bound as-is is the defect under test");
        }

        [Fact]
        public void InCondition_WithNegation_BindsUnderlyingIntegersAndEmitsNotIn()
        {
            var condition = Condition.Create(
                "State",
                new object[] { EnumModelState.Created, EnumModelState.Confirmed },
                ConditionType.In,
                isNot: true);

            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);

            sql.Should().Be("State NOT IN (@WHEREState0_0, @WHEREState1_1)");
            _command.TestParameters.All.Select(p => p.Value).Should().Equal(new object[] { 0, 1 });
        }

        [Fact]
        public void EqualCondition_WithEnumValue_BindsUnderlyingInteger()
        {
            var condition = Condition.CreateValue("State", EnumModelState.Processing);

            _connectorMock.Object.ConditionDefinition(condition, _command);

            _command.TestParameters.All.Should().HaveCount(1);
            _command.TestParameters.All[0].Value.Should().Be(3);
        }

        [Fact]
        public void ContainsOverEnumCollection_TranslatesToInCondition()
        {
            // Translation-level half: the predicate shape must reach the connector as an IN condition
            // over the enum column at all (it does — the loss was purely in value conversion).
            var states = new[] { EnumModelState.Paid, EnumModelState.Shipped };
            Expression<Func<EnumModel, bool>> expr = x => states.Contains(x.State);

            var leaf = Birko.Data.SQL.DataBase.ParseConditionExpression(expr)
                .SelectMany(Flatten)
                .Single(c => c.Name != null && c.Name.EndsWith("State"));

            leaf.Type.Should().Be(ConditionType.In);
            leaf.Values.Should().NotBeNull();
            leaf.Values!.Cast<object>().Should().HaveCount(2);
        }

        [Fact]
        public void ContainsWithExplicitComparer_StillTranslatesToInOverTheColumn()
        {
            // A NON-null comparer must be skipped as well — its semantics are delegated to the column
            // collation (same contract as the StringComparison overloads of the string pattern methods).
            // Parsing it as a value is what corrupted the condition.
            var names = new[] { "alpha", "beta" };
            Expression<Func<EnumModel, bool>> expr = x => names.Contains(x.Name, StringComparer.OrdinalIgnoreCase);

            var leaf = Birko.Data.SQL.DataBase.ParseConditionExpression(expr)
                .SelectMany(Flatten)
                .Single(c => c.Name != null && c.Name.EndsWith("Name"));

            leaf.Type.Should().Be(ConditionType.In);
            leaf.Values!.Cast<object>().Should().Equal("alpha", "beta");
        }

        [Theory]
        [InlineData(EnumModelState.Created, 0)]
        [InlineData(EnumModelState.Cancelled, 5)]
        public void NormalizeParameterValue_UnwrapsIntBackedEnum(EnumModelState state, int expected)
        {
            var normalized = AbstractConnectorBase.NormalizeParameterValue(state);

            normalized.Should().Be(expected);
            normalized.Should().BeOfType<int>();
        }

        [Fact]
        public void NormalizeParameterValue_UnwrapsNonIntBackedEnums()
        {
            AbstractConnectorBase.NormalizeParameterValue(ByteBacked.Two).Should().BeOfType<byte>().And.Be((byte)2);
            AbstractConnectorBase.NormalizeParameterValue(LongBacked.Big).Should().BeOfType<long>().And.Be(9_000_000_000L);
        }

        [Fact]
        public void NormalizeParameterValue_PassesThroughNonEnumValues()
        {
            AbstractConnectorBase.NormalizeParameterValue(null).Should().BeNull();
            AbstractConnectorBase.NormalizeParameterValue("text").Should().Be("text");
            AbstractConnectorBase.NormalizeParameterValue(42).Should().Be(42);
            var guid = Guid.NewGuid();
            AbstractConnectorBase.NormalizeParameterValue(guid).Should().Be(guid);
        }

        private static IEnumerable<Condition> Flatten(Condition condition)
        {
            if (condition.SubConditions?.Any() == true)
            {
                foreach (var sub in condition.SubConditions)
                    foreach (var flat in Flatten(sub))
                        yield return flat;
            }
            else
            {
                yield return condition;
            }
        }

        private enum ByteBacked : byte { One = 1, Two = 2 }

        private enum LongBacked : long { Big = 9_000_000_000L }
    }
}
