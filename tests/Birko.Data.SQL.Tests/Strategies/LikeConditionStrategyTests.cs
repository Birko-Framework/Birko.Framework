using System.Data;
using System.Data.Common;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Connectors.Strategies;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Moq;
using Moq.Protected;
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
            _connectorMock = new Mock<AbstractConnector>(new Birko.Configuration.PasswordSettings()) { CallBase = true };
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
            sql.Should().Be("Name LIKE @WHEREName0_0 ESCAPE '!'");
        }

        [Fact]
        public void BuildSql_Like_IsNot_ShouldReturnNotLikeOperator()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "test", ConditionType.Like, isNot: true);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name NOT LIKE @WHEREName0_0 ESCAPE '!'");
        }

        [Fact]
        public void BuildSql_StartsWith_ShouldFormatValueWithStartingWildcard()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "John", ConditionType.StartsWith);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE @WHEREName0_0 ESCAPE '!'");
        }

        [Fact]
        public void BuildSql_EndsWith_ShouldFormatValueWithEndingWildcard()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", "Doe", ConditionType.EndsWith);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE @WHEREName0_0 ESCAPE '!'");
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
            sql.Should().Be("Table1.Name LIKE Table2.Name ESCAPE '!'");
        }

        [Fact]
        public void BuildSql_ShouldHandleNullValues()
        {
            // Arrange
            var condition = Condition.CreateValue("Name", null, ConditionType.Like);

            // Act
            var sql = _strategy.BuildSql(condition, _command, _context);

            // Assert
            sql.Should().Be("Name LIKE NULL ESCAPE '!'");
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

        /// <summary>A dialect that refuses a pattern past <see cref="Limit"/> and records every pattern it is asked about.</summary>
        private sealed class LimitedDialect : SqlValueFragmentDialect
        {
            public const int Limit = 10;
            public List<string> Patterns { get; } = new();

            public override void RequireLikePattern(string pattern, string form)
            {
                Patterns.Add(pattern);
                if (pattern.Length > Limit) throw new NotSupportedException($"{form}: {pattern.Length} past {Limit}");
            }
        }

        private static (SqlBuilderContext Context, LimitedDialect Dialect) Limited()
        {
            var dialect = new LimitedDialect();
            var connector = new Mock<AbstractConnector>(new Birko.Configuration.PasswordSettings()) { CallBase = true };
            connector.Protected().Setup<SqlValueFragmentDialect>("FragmentDialect").Returns(dialect);
            return (new SqlBuilderContext(connector.Object), dialect);
        }

        /// <summary>
        /// TASK-874 — the no-argument forms ask the provider's dialect about the pattern they bind (escaped, with its
        /// wildcards), so SQL Server can refuse one past its limit before anything runs.
        /// </summary>
        [Theory]
        [InlineData(ConditionType.Like, "a%b", "%a!%b%")]
        [InlineData(ConditionType.StartsWith, "abc", "abc%")]
        [InlineData(ConditionType.EndsWith, "abc", "%abc")]
        public void BuildSql_AsksTheDialectAboutTheBoundPattern(ConditionType type, string term, string pattern)
        {
            var (context, dialect) = Limited();
            _strategy.BuildSql(Condition.CreateValue("Name", term, type), _command, context);
            dialect.Patterns.Should().Equal(pattern);
        }

        [Theory]
        [InlineData(ConditionType.Like, "aaaaaaaaa", false, "a Contains on Name")]
        [InlineData(ConditionType.Like, "%%%%%", false, "a Contains on Name")]
        [InlineData(ConditionType.Like, "aaaaaaaaa", true, "a Contains under NOT on Name")]
        [InlineData(ConditionType.StartsWith, "aaaaaaaaaaa", false, "a StartsWith on Name")]
        [InlineData(ConditionType.EndsWith, "aaaaaaaaaaa", false, "an EndsWith on Name")]
        public void BuildSql_RefusesAPatternTheDialectRefuses_BeforeBindingIt(ConditionType type, string term, bool isNot, string form)
        {
            var (context, _) = Limited();
            var build = () => _strategy.BuildSql(Condition.CreateValue("Name", term, type, isNot), _command, context);
            build.Should().Throw<NotSupportedException>().WithMessage($"{form}:*");
            _command.TestParameters.All.Should().BeEmpty("nothing was bound");
        }

        [Fact]
        public void BuildSql_AtTheLimit_Renders()
        {
            var (context, _) = Limited();
            _strategy.BuildSql(Condition.CreateValue("Name", "aaaaaaaa", ConditionType.Like), _command, context)
                .Should().StartWith("Name LIKE ");
        }

        [Fact]
        public void BuildSql_AColumnPattern_IsNotAskedAbout()
        {
            var (context, dialect) = Limited();
            _strategy.BuildSql(Condition.AndField("Name", "Other", ConditionType.Like), _command, context);
            dialect.Patterns.Should().BeEmpty();
        }
    }
}
