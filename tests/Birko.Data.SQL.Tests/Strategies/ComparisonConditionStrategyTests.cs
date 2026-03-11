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
    public class ComparisonConditionStrategyTests
    {
        private readonly ComparisonConditionStrategy _strategy;
        private readonly TestDbCommand _command;
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly SqlBuilderContext _context;

        public ComparisonConditionStrategyTests()
        {
            _strategy = new ComparisonConditionStrategy();
            _command = new TestDbCommand();
            _connectorMock = new Mock<AbstractConnector>(new Birko.Data.Stores.PasswordSettings()) { CallBase = true };
            _context = new SqlBuilderContext(_connectorMock.Object);
        }

        [Theory]
        [InlineData(ConditionType.Less)]
        [InlineData(ConditionType.Greather)]
        [InlineData(ConditionType.LessAndEqual)]
        [InlineData(ConditionType.GreatherAndEqual)]
        public void CanHandle_ShouldReturnTrue_ForComparisonConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(ConditionType.Equal)]
        [InlineData(ConditionType.Like)]
        [InlineData(ConditionType.In)]
        [InlineData(ConditionType.IsNull)]
        public void CanHandle_ShouldReturnFalse_ForNonComparisonConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void BuildSql_Less_ShouldReturnLessThanOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 25, ConditionType.Less);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age < @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_Less_IsNot_ShouldReturnGreaterThanOrEqualOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 25, ConditionType.Less, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age >= @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_Greather_ShouldReturnGreaterThanOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 18, ConditionType.Greather);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age > @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_Greather_IsNot_ShouldReturnLessThanOrEqualOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 18, ConditionType.Greather, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age <= @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_LessAndEqual_ShouldReturnLessThanOrEqualOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Price", 100m, ConditionType.LessAndEqual);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Price <= @WHEREPrice0_0");
        }

        [Fact]
        public void BuildSql_LessAndEqual_IsNot_ShouldReturnGreaterThanOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Price", 100m, ConditionType.LessAndEqual, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Price > @WHEREPrice0_0");
        }

        [Fact]
        public void BuildSql_GreatherAndEqual_ShouldReturnGreaterThanOrEqualOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 21, ConditionType.GreatherAndEqual);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age >= @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_GreatherAndEqual_IsNot_ShouldReturnLessThanOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 21, ConditionType.GreatherAndEqual, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age < @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_ShouldHandleFieldComparisons()
        {
            // Arrange
            var condition = Condition.AndField("Table1.Field1", "Table2.Field2", ConditionType.Greather);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Table1.Field1 > Table2.Field2");
        }

        [Fact]
        public void BuildSql_ShouldThrow_WhenConditionIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _strategy.BuildSql(null!, _command, _context));
        }
    }
}
