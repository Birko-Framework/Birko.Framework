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
    public class LikeConditionStrategyTests
    {
        private readonly LikeConditionStrategy _strategy;
        private readonly TestDbCommand _command;
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly SqlBuilderContext _context;

        public LikeConditionStrategyTests()
        {
            _strategy = new LikeConditionStrategy();
            _command = new TestDbCommand();
            _connectorMock = new Mock<AbstractConnector>(new Birko.Data.Stores.PasswordSettings()) { CallBase = true };
            _context = new SqlBuilderContext(_connectorMock.Object);
        }

        [Theory]
        [InlineData(ConditionType.Like)]
        [InlineData(ConditionType.StartsWith)]
        [InlineData(ConditionType.EndsWith)]
        public void CanHandle_ShouldReturnTrue_ForLikeConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(ConditionType.Equal)]
        [InlineData(ConditionType.Less)]
        [InlineData(ConditionType.In)]
        [InlineData(ConditionType.IsNull)]
        public void CanHandle_ShouldReturnFalse_ForNonLikeConditionTypes(ConditionType type)
        {
            // Act
            var result = _strategy.CanHandle(type);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void BuildSql_Like_ShouldReturnLikeOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "test", ConditionType.Like);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE @WHEREName0_0");
        }

        [Fact]
        public void BuildSql_Like_IsNot_ShouldReturnNotLikeOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "test", ConditionType.Like, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name NOT LIKE @WHEREName0_0");
        }

        [Fact]
        public void BuildSql_StartsWith_ShouldFormatValueWithStartingWildcard()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "John", ConditionType.StartsWith);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE @WHEREName0_0");
        }

        [Fact]
        public void BuildSql_EndsWith_ShouldFormatValueWithEndingWildcard()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "Doe", ConditionType.EndsWith);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE @WHEREName0_0");
        }

        [Fact]
        public void BuildSql_ShouldAddParameterWithFormattedValue()
        {
            // Arrange
            var condition = Condition.CreateValue("Email", "example.com", ConditionType.EndsWith);

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().HaveCount(1);
            _command.TestParameters.All.Should().Contain(p => p.ParameterName == "@WHEREEmail0_0");
            _command.TestParameters.All.Should().Contain(p => (string)p.Value! == "%example.com");
        }

        [Fact]
        public void BuildSql_StartsWith_ShouldAddParameterWithCorrectWildcard()
        {
            // Arrange
            var condition = Condition.CreateValue("Title", "Mr.", ConditionType.StartsWith);

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().Contain(p => (string)p.Value! == "Mr.%");
        }

        [Fact]
        public void BuildSql_Like_ShouldAddParameterWithBothWildcards()
        {
            // Arrange
            var condition = Condition.CreateValue("Description", "keyword", ConditionType.Like);

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().Contain(p => (string)p.Value! == "%keyword%");
        }

        [Fact]
        public void BuildSql_ShouldHandleFieldReferences()
        {
            // Arrange
            var condition = Condition.AndField("Table1.Name", "Table2.Name", ConditionType.Like);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Table1.Name LIKE Table2.Name");
        }

        [Fact]
        public void BuildSql_ShouldHandleNullValues()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", null, ConditionType.Like);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE NULL");
        }

        [Fact]
        public void BuildSql_ShouldThrow_WhenConditionIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _strategy.BuildSql(null!, _command, _context));
        }

        [Fact]
        public void BuildSql_ShouldNotAddWildcardToNonStringValues()
        {
            // Arrange
            var condition = Condition.CreateValue("Code", 12345, ConditionType.Like);

            // Act
            _strategy.BuildSql(condition, _command, _context);

            // Assert
            _command.TestParameters.All.Should().Contain(p => p.Value!.Equals(12345));
        }
    }
}
