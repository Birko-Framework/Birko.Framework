using System.Data;
using System.Data.Common;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Birko.Data.SQL.Tests.Connectors
{
    public class AbstractConnectorConditionBuilderTests
    {
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly TestDbCommand _command;

        public AbstractConnectorConditionBuilderTests()
        {
            _connectorMock = new Mock<AbstractConnector>(new Birko.Configuration.PasswordSettings());
            _command = new TestDbCommand();
            _connectorMock.CallBase = true;
        }

        [Fact]
        public void ConditionDefinition_SingleEqualCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("Name", "John");
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Name = @WHEREName0_0");
        }

        [Fact]
        public void ConditionDefinition_SingleLessCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("Age", 18, ConditionType.Less);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Age < @WHEREAge0_0");
        }

        [Fact]
        public void ConditionDefinition_SingleGreaterCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("Price", 100m, ConditionType.Greather);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Price > @WHEREPrice0_0");
        }

        [Fact]
        public void ConditionDefinition_LikeCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("Email", "@example.com", ConditionType.EndsWith);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Email LIKE @WHEREEmail0_0");
        }

        [Fact]
        public void ConditionDefinition_InCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.Create("Status", new object[] { "Active", "Pending", "Inactive" }, ConditionType.In);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Status IN (@WHEREStatus0_0, @WHEREStatus1_1, @WHEREStatus2_2)");
        }

        [Fact]
        public void ConditionDefinition_IsNullCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("DeletedAt", null, ConditionType.IsNull);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("DeletedAt IS NULL");
        }

        [Fact]
        public void ConditionDefinition_IsNotNullCondition_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("DeletedAt", null, ConditionType.IsNull, isNot: true);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("DeletedAt IS NOT NULL");
        }

        [Fact]
        public void ConditionDefinition_NullCondition_ShouldReturnEmptyString()
        {
            var sql = _connectorMock.Object.ConditionDefinition((Condition)null, _command);
            sql.Should().BeEmpty();
        }

        [Fact]
        public void ConditionDefinition_SingleSubCondition_ShouldNotWrapInParentheses()
        {
            var subCondition = Condition.CreateValue("Age", 18);
            var condition = Condition.AndSubCondition(new[] { subCondition });
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Age = @WHEREAge0_0");
        }

        [Fact]
        public void ConditionDefinition_MultipleSubConditions_ShouldWrapInParentheses()
        {
            var subConditions = new[]
            {
                Condition.CreateValue("Age", 18, ConditionType.GreatherAndEqual),
                Condition.CreateValue("Age", 65, ConditionType.Less)
            };
            var condition = Condition.AndSubCondition(subConditions);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("(Age >= @WHEREAge0_0 AND Age < @WHEREAge0_1)");
        }

        [Fact]
        public void ConditionDefinition_NestedSubConditions_ShouldHandleCorrectly()
        {
            var innerConditions = new[]
            {
                Condition.CreateValue("City", "New York"),
                Condition.CreateValue("City", "Los Angeles")
            };
            var innerSubCondition = Condition.OrSubCondition(innerConditions);

            var outerConditions = new[]
            {
                Condition.CreateValue("Country", "USA"),
                innerSubCondition
            };
            var condition = Condition.AndSubCondition(outerConditions);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Contain("Country =");
            sql.Should().Contain("OR");
        }

        [Fact]
        public void ConditionDefinition_WithNotOperator_ShouldGenerateCorrectSql()
        {
            var condition = Condition.CreateValue("Status", "Deleted", isNot: true);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Status <> @WHEREStatus0_0");
        }

        [Fact]
        public void ConditionDefinition_FieldComparison_ShouldGenerateCorrectSql()
        {
            var condition = Condition.AndField("Table1.Field1", "Table2.Field2", ConditionType.Greather);
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Table1.Field1 > Table2.Field2");
        }

        [Fact]
        public void ConditionDefinition_MultipleConditions_ShouldJoinWithAnd()
        {
            var conditions = new[]
            {
                Condition.CreateValue("Name", "John"),
                Condition.CreateValue("Age", 25, ConditionType.GreatherAndEqual),
                Condition.CreateValue("Status", "Active")
            };
            var sql = _connectorMock.Object.ConditionDefinition(conditions, _command);
            sql.Should().Be("Name = @WHEREName0_0 AND Age >= @WHEREAge0_1 AND Status = @WHEREStatus0_2");
        }

        [Fact]
        public void ConditionDefinition_MultipleConditions_WithOr_ShouldJoinWithOr()
        {
            var conditions = new[]
            {
                Condition.CreateValue("Status", "Active"),
                Condition.OrValue<string>("Status", "Pending")
            };
            var sql = _connectorMock.Object.ConditionDefinition(conditions, _command);
            sql.Should().Be("Status = @WHEREStatus0_0 OR Status = @WHEREStatus0_1");
        }

        [Fact]
        public void ConditionDefinition_EmptyConditionsList_ShouldReturnEmptyString()
        {
            var conditions = Array.Empty<Condition>();
            var sql = _connectorMock.Object.ConditionDefinition(conditions, _command);
            sql.Should().BeEmpty();
        }

        [Fact]
        public void ConditionDefinition_NullConditionsList_ShouldReturnEmptyString()
        {
            var sql = _connectorMock.Object.ConditionDefinition((IEnumerable<Condition>)null, _command);
            sql.Should().BeEmpty();
        }

        [Fact]
        public void ConditionDefinition_ComplexScenario_ShouldHandleCorrectly()
        {
            var ageConditions = new[]
            {
                Condition.CreateValue("Age", 18, ConditionType.GreatherAndEqual),
                Condition.CreateValue("Age", 65, ConditionType.Less)
            };
            var ageSubCondition = Condition.AndSubCondition(ageConditions);

            var statusConditions = new[]
            {
                Condition.CreateValue("Status", "Active"),
                Condition.OrValue<string>("Status", "Pending")
            };
            var statusSubCondition = Condition.OrSubCondition(statusConditions);

            var finalConditions = new[]
            {
                ageSubCondition,
                statusSubCondition
            };

            var sql = _connectorMock.Object.ConditionDefinition(finalConditions, _command);
            sql.Should().Contain("Age >=");
            sql.Should().Contain("Age <");
            sql.Should().Contain("Status =");
            sql.Should().Contain("OR");
            sql.Should().Contain("AND");
        }

        [Fact]
        public void ConditionDefinition_DottedFieldName_ShouldSanitizeInParameterName()
        {
            var condition = Condition.CreateValue("Users.Name", "John");
            var sql = _connectorMock.Object.ConditionDefinition(condition, _command);
            sql.Should().Be("Users.Name = @WHEREUsersName0_0");
        }

        [Fact]
        public void ConditionDefinition_ShouldAddParametersCorrectly()
        {
            var condition = Condition.CreateValue("Name", "John");
            _connectorMock.Object.ConditionDefinition(condition, _command);
            _command.TestParameters.All.Should().HaveCount(1);
            _command.TestParameters.All[0].ParameterName.Should().Be("@WHEREName0_0");
            _command.TestParameters.All[0].Value.Should().Be("John");
        }
    }
}
