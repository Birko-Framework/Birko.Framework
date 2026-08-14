using System.Data;
using System.Data.Common;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Connectors.Strategies;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Birko.Data.SQL.Tests.Strategies
{
    public class InConditionStrategyTests
    {
        private readonly InConditionStrategy _strategy;
        private readonly TestDbCommand _command;
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly SqlBuilderContext _context;

        public InConditionStrategyTests()
        {
            _strategy = new InConditionStrategy();
            _command = new TestDbCommand();
            _connectorMock = new Mock<AbstractConnector>(new Birko.Configuration.PasswordSettings()) { CallBase = true };
            _context = new SqlBuilderContext(_connectorMock.Object);
        }

        [Fact]
        public void CanHandle_ShouldReturnTrue_ForInConditionType()
        {
            // Act
            var result = _strategy.CanHandle(ConditionType.In);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(ConditionType.Equal)]
        [InlineData(ConditionType.Less)]
        [InlineData(ConditionType.Like)]
        [InlineData(ConditionType.IsNull)]
        public void CanHandle_ShouldReturnFalse_ForNonInConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void BuildSql_ShouldReturnInOperator_WhenIsNotIsFalse()
        {
            // Arrange
            var condition = Condition.Create("Status", new object[] { "Active", "Pending" }, ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Status IN (@WHEREStatus0_0, @WHEREStatus1_1)");
        }

        [Fact]
        public void BuildSql_ShouldReturnNotInOperator_WhenIsNotIsTrue()
        {
            // Arrange
            var condition = Condition.Create("Status", new object[] { "Deleted" }, ConditionType.In, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Status NOT IN (@WHEREStatus0_0)");
        }

        [Fact]
        public void BuildSql_ShouldAddParametersForEachValue()
        {
            // Arrange
            var condition = Condition.Create("Id", new object[] { 1, 2, 3, 4, 5 }, ConditionType.In);

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().HaveCount(5);
        }

        [Fact]
        public void BuildSql_SingleValue_ShouldReturnSingleParameter()
        {
            // Arrange
            var condition = Condition.Create("Category", new object[] { "Books" }, ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Category IN (@WHERECategory0_0)");
        }

        // ── Empty value set ───────────────────────────────────────────────────────────────────────────
        // These two tests previously asserted `Status IN ()`, i.e. they pinned the defect as the contract.
        // `IN ()` is a syntax error on PostgreSQL and MSSQL. SQLite's grammar permits it and evaluates it
        // as always-false, which is why the bug survived: the SQLite-backed suites agreed with it. An empty
        // set is now rendered as a constant with the same semantics — and note the NOT IN case must be
        // always-TRUE, since "not in the empty set" is true of every row.

        [Fact]
        public void BuildSql_EmptyValues_ShouldReturnAlwaysFalseConstant()
        {
            // Arrange
            var condition = Condition.Create("Status", Array.Empty<object?>(), ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert — never `IN ()`, which PostgreSQL/MSSQL reject outright.
            sql.Should().Be("1 = 0");
            sql.Should().NotContain("IN ()");
            _command.Parameters.Count.Should().Be(0, "a constant predicate needs no parameters");
        }

        [Fact]
        public void BuildSql_NullValues_ShouldReturnAlwaysFalseConstant()
        {
            // Arrange
            var condition = Condition.Create("Status", null, ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("1 = 0");
            sql.Should().NotContain("IN ()");
        }

        // TASK-137 — the two tests below previously asserted `1 = 1` for the empty NOT IN, i.e. they pinned a
        // guard bypass as the contract. An empty NOT IN does match every row, but a tautology is the wrong way
        // to say so: `1 = 1` is a non-empty WHERE that constrains nothing, so it satisfied
        // AbstractConnectorBase.AddRequiredWhere's whole-table write guard and `Delete(x => !empty.Contains(x))`
        // emptied the table, silently (measured: 0 of 3 rows left, no exception). The term now has NO rendering
        // at this layer — it is reduced away by the enclosing chain, where the AND/OR context is known.

        [Fact]
        public void BuildSql_EmptyValues_WithIsNot_Throws_BecauseAnAlwaysTrueTermHasNoRendering()
        {
            // Arrange — every row is outside the empty set, so this matches EVERYTHING. It must not invert to
            // "matches nothing", and it must not become a constant either.
            var condition = Condition.Create("Status", Array.Empty<object?>(), ConditionType.In, isNot: true);

            // Act
            var act = () => _strategy.BuildSql(condition, _command, _context);

            // Assert — a throw, not `""`: an empty string would be joined into the chain by its neighbours'
            // separators and produce `A AND  AND B`, which is the silent version of the same bug.
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*matches every row*")
                .WithMessage("*IsAlwaysTrueCondition*");
        }

        [Fact]
        public void BuildSql_NullValues_WithIsNot_Throws_BecauseAnAlwaysTrueTermHasNoRendering()
        {
            // Arrange
            var condition = Condition.Create("Status", null, ConditionType.In, isNot: true);

            // Act
            var act = () => _strategy.BuildSql(condition, _command, _context);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void BuildSql_NeverEmitsTheInjectionLookalike_ForAnyEmptySetShape()
        {
            // The property the fix actually owes, asserted positively rather than as "the old string is gone"
            // from one input: no empty-set shape this strategy can be handed renders `1 = 1`.
            foreach (var isNot in new[] { false, true })
            {
                foreach (System.Collections.IEnumerable? values in new System.Collections.IEnumerable?[] { null, Array.Empty<object?>() })
                {
                    var condition = new Condition("Status", values, ConditionType.In, isNot: isNot);
                    string? sql = null;
                    try { sql = _strategy.BuildSql(condition, _command, _context); }
                    catch (InvalidOperationException) { /* the always-true half refuses, asserted above */ }

                    sql?.Should().NotContain("1 = 1",
                        $"isNot={isNot} must never render the `' OR 1=1--` signature");
                }
            }
        }

        [Fact]
        public void BuildSql_SingleValue_IsUnaffectedByTheEmptySetGuard()
        {
            // Arrange — a one-element set must still render a real IN, not the constant. Guards against an
            // over-eager emptiness check (e.g. one that mistook a single value for "no values").
            var condition = Condition.Create("Status", new object[] { "active" }, ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Contain(" IN (");
            sql.Should().NotBe("1 = 0");
            _command.Parameters.Count.Should().Be(1);
        }

        [Fact]
        public void BuildSql_CollectionOfOnlyNulls_StillRendersARealInClause()
        {
            // Arrange — the strategy counts ELEMENTS, it does not filter nulls. A list of nulls is not an
            // empty list, so it still renders `IN (@p)`; `Col IN (NULL)` is never true in SQL, so the
            // result is the same "matches nothing" either way. Documents where the boundary sits, so the
            // guard is not later "fixed" into filtering nulls here (the parser already does that upstream).
            var condition = Condition.Create("Status", new object?[] { null }, ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Contain(" IN (");
            sql.Should().NotBe("1 = 0");
        }

        [Fact]
        public void BuildSql_WithFieldReferences_ShouldNotAddParameters()
        {
            // Arrange
            var condition = new Condition(
                "Table1.Status",
                new object?[] { "Table2.Status" },
                ConditionType.In,
                isField: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Table1.Status IN (Table2.Status)");
            _command.TestParameters.All.Should().HaveCount(0);
        }

        [Fact]
        public void BuildSql_WithMultipleFieldReferences_ShouldNotAddParameters()
        {
            // Arrange
            var condition = new Condition(
                "Table1.Status",
                new object?[] { "Table2.Status1", "Table2.Status2", "Table2.Status3" },
                ConditionType.In,
                isField: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Table1.Status IN (Table2.Status1, Table2.Status2, Table2.Status3)");
            _command.TestParameters.All.Should().HaveCount(0);
        }

        [Fact]
        public void BuildSql_ShouldHandleMixedTypes()
        {
            // Arrange
            var condition = Condition.Create(
                "MixedColumn",
                new object[] { "string", 123, 45.67m, true },
                ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("MixedColumn IN (@WHEREMixedColumn0_0, @WHEREMixedColumn1_1, @WHEREMixedColumn2_2, @WHEREMixedColumn3_3)");
            _command.TestParameters.All.Should().HaveCount(4);
        }

        [Fact]
        public void BuildSql_ShouldHandleNullValuesInCollection()
        {
            // Arrange
            var condition = Condition.Create(
                "NullableColumn",
                new object?[] { "Value1", null, "Value3" },
                ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("NullableColumn IN (@WHERENullableColumn0_0, @WHERENullableColumn1_1, @WHERENullableColumn2_2)");
            _command.TestParameters.All.Should().HaveCount(3);
        }

        [Fact]
        public void BuildSql_ShouldThrow_WhenConditionIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _strategy.BuildSql(null!, _command, _context));
        }

        [Fact]
        public void BuildSql_ShouldHandleGuidValues()
        {
            // Arrange
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var condition = Condition.Create(
                "UserId",
                new object[] { guid1, guid2 },
                ConditionType.In);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be($"UserId IN (@WHEREUserId0_0, @WHEREUserId1_1)");
        }
    }
}
