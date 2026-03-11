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
    public class EqualConditionStrategyTests
    {
        private readonly EqualConditionStrategy _strategy;
        private readonly TestDbCommand _command;
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly SqlBuilderContext _context;

        public EqualConditionStrategyTests()
        {
            _strategy = new EqualConditionStrategy();
            _command = new TestDbCommand();
            _connectorMock = new Mock<AbstractConnector>(new Birko.Data.Stores.PasswordSettings()) { CallBase = true };
            _context = new SqlBuilderContext(_connectorMock.Object);
        }

        [Fact]
        public void CanHandle_ShouldReturnTrue_ForEqualConditionType()
        {
            // Act
            var result = _strategy.CanHandle(ConditionType.Equal);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(ConditionType.Less)]
        [InlineData(ConditionType.Greather)]
        [InlineData(ConditionType.Like)]
        [InlineData(ConditionType.In)]
        [InlineData(ConditionType.IsNull)]
        public void CanHandle_ShouldReturnFalse_ForNonEqualConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void BuildSql_ShouldReturnEqualOperator_WhenIsNotIsFalse()
        {
            // Arrange
            var condition = Condition.CreateValue("FieldName", "testValue");

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("FieldName = @WHEREFieldName0_0");
        }

        [Fact]
        public void BuildSql_ShouldReturnNotEqualOperator_WhenIsNotIsTrue()
        {
            // Arrange
            var condition = Condition.CreateValue("FieldName", "testValue", isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("FieldName <> @WHEREFieldName0_0");
        }

        [Fact]
        public void BuildSql_ShouldAddParameter_WhenValueIsNotAField()
        {
            // Arrange
            var condition = Condition.CreateValue("UserName", "john.doe");

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().HaveCount(1);
        }

        [Fact]
        public void BuildSql_ShouldUseFieldName_WhenValueIsAField()
        {
            // Arrange
            var condition = Condition.AndField("Table1.Field1", "Table2.Field2");

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Table1.Field1 = Table2.Field2");
        }

        [Fact]
        public void BuildSql_ShouldThrow_WhenConditionIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _strategy.BuildSql(null!, _command, _context));
        }

        [Fact]
        public void BuildSql_ShouldHandleNullValues()
        {
            // Arrange
            var condition = Condition.CreateValue("FieldName", null);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("FieldName = NULL");
        }

        [Fact]
        public void BuildSql_ShouldHandleEmptyValues()
        {
            // Arrange
            var condition = Condition.Create("FieldName", Array.Empty<object?>());

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("FieldName = NULL");
        }

        [Fact]
        public void BuildSql_ShouldHandleNumericValues()
        {
            // Arrange
            var condition = Condition.CreateValue("Age", 25);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Age = @WHEREAge0_0");
        }

        [Fact]
        public void BuildSql_ShouldHandleDateValues()
        {
            // Arrange
            var testDate = new DateTime(2025, 2, 26);
            var condition = Condition.CreateValue("CreatedDate", testDate);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("CreatedDate = @WHERECreatedDate0_0");
        }
    }
}
