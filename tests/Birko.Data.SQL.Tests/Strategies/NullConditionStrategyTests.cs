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
    public class NullConditionStrategyTests
    {
        private readonly NullConditionStrategy _strategy;
        private readonly TestDbCommand _command;
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly SqlBuilderContext _context;

        public NullConditionStrategyTests()
        {
            _strategy = new NullConditionStrategy();
            _command = new TestDbCommand();
            _connectorMock = new Mock<AbstractConnector>(new Birko.Data.Stores.PasswordSettings()) { CallBase = true };
            _context = new SqlBuilderContext(_connectorMock.Object);
        }

        [Fact]
        public void CanHandle_ShouldReturnTrue_ForIsNullConditionType()
        {
            // Act
            var result = _strategy.CanHandle(ConditionType.IsNull);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(ConditionType.Equal)]
        [InlineData(ConditionType.Less)]
        [InlineData(ConditionType.Like)]
        [InlineData(ConditionType.In)]
        public void CanHandle_ShouldReturnFalse_ForNonIsNullConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void BuildSql_ShouldReturnIsNullOperator_WhenIsNotIsFalse()
        {
            // Arrange
            var condition = Condition.CreateValue("DeletedDate", null, ConditionType.IsNull);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("DeletedDate IS NULL");
        }

        [Fact]
        public void BuildSql_ShouldReturnIsNotNullOperator_WhenIsNotIsTrue()
        {
            // Arrange
            var condition = Condition.CreateValue("DeletedDate", null, ConditionType.IsNull, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("DeletedDate IS NOT NULL");
        }

        [Fact]
        public void BuildSql_ShouldNotAddAnyParameters()
        {
            // Arrange
            var condition = Condition.CreateValue("OptionalField", null, ConditionType.IsNull);

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().HaveCount(0);
        }

        [Fact]
        public void BuildSql_ShouldWorkWithVariousFieldNames()
        {
            // Arrange
            var condition1 = Condition.CreateValue("SimpleField", null, ConditionType.IsNull);
            var condition2 = Condition.CreateValue("Table.Field", null, ConditionType.IsNull);
            var condition3 = Condition.CreateValue("Schema.Table.Field", null, ConditionType.IsNull);

            // Act
            var sql1 = _strategy.BuildSql(condition1, _command, _context);
            var sql2 = _strategy.BuildSql(condition2, _command, _context);
            var sql3 = _strategy.BuildSql(condition3, _command, _context);

            // Assert
            sql1.Should().Be("SimpleField IS NULL");
            sql2.Should().Be("Table.Field IS NULL");
            sql3.Should().Be("Schema.Table.Field IS NULL");
        }

        [Fact]
        public void BuildSql_ShouldIgnoreValuesInCondition()
        {
            // Arrange
            // Even if values are provided, IS NULL should ignore them
            var condition = Condition.Create(
                "Field",
                new object?[] { "value1", "value2" },
                ConditionType.IsNull);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Field IS NULL");
            _command.TestParameters.All.Should().HaveCount(0);
        }

        [Fact]
        public void BuildSql_ShouldThrow_WhenConditionIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _strategy.BuildSql(null!, _command, _context));
        }

        [Fact]
        public void BuildSql_ShouldThrow_WhenConditionNameIsEmpty()
        {
            // Arrange
            var condition = new Condition(
                string.Empty,
                null,
                ConditionType.IsNull);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                _strategy.BuildSql(condition, _command, _context));
        }

        [Fact]
        public void BuildSql_ShouldNotConsiderIsFieldFlag()
        {
            // Arrange
            // IsField flag should be ignored for IS NULL checks
            var condition = new Condition(
                "MyField",
                new object?[] { "OtherField" },
                ConditionType.IsNull,
                isField: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("MyField IS NULL");
        }

        [Theory]
        [InlineData("NullableInt")]
        [InlineData("OptionalString")]
        [InlineData("DeletedAt")]
        [InlineData("ArchivedFlag")]
        public void BuildSql_ShouldHandleCommonNullableFieldNames(string fieldName)
        {
            // Arrange
            var condition = Condition.CreateValue(fieldName, null, ConditionType.IsNull);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be($"{fieldName} IS NULL");
        }

        [Fact]
        public void BuildSql_ShouldHandleIsNotWithCommonUseCases()
        {
            // Arrange
            var activeRecordsCondition = Condition.CreateValue(
                "DeletedAt",
                null,
                ConditionType.IsNull,
                isNot: true);

            // Act
            var sql = _strategy.BuildSql(activeRecordsCondition, _command, _context);

            // Assert
            sql.Should().Be("DeletedAt IS NOT NULL");
        }
    }
}
