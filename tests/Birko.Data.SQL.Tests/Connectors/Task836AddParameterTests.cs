using System.Linq;
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
    /// TASK-836: <c>AddParameter</c> looked every name up before adding it, a linear scan on SQLite's and SQL Server's
    /// collections, so an IN list of n values cost n scans. A name the renderer generated skips the lookup; every
    /// other name keeps it, because a re-added name must overwrite (the multi-row insert re-binds the same names per row).
    /// </summary>
    public class Task836AddParameterTests
    {
        private readonly AbstractConnector _connector;
        private readonly TestDbCommand _command = new();

        public Task836AddParameterTests()
        {
            _connector = new Mock<AbstractConnector>(new Birko.Configuration.PasswordSettings()) { CallBase = true }.Object;
        }

        [Fact]
        public void An_in_list_is_rendered_without_looking_any_name_up()
        {
            var values = Enumerable.Range(0, 100).Cast<object>().ToArray();

            var sql = _connector.ConditionDefinition(Condition.Create("Id", values, ConditionType.In), _command);

            _command.TestParameters.ContainsByNameCalls.Should().Be(0, "every name in the list was generated, so none can exist yet");
            _command.TestParameters.All.Select(p => p.Value).Should().Equal(values);
            _command.TestParameters.All.Select(p => p.ParameterName).Should().OnlyHaveUniqueItems();
            sql.Should().StartWith("Id IN (@WHEREId0_0, @WHEREId1_1, ").And.EndWith("@WHEREId99_99)");
        }

        [Fact]
        public void The_generated_path_keeps_the_value_conversion()
        {
            var sql = _connector.ConditionDefinition(
                Condition.Create("State", new object[] { EnumModelState.Paid }, ConditionType.In), _command);

            sql.Should().Be("State IN (@WHEREState0_0)");
            _command.TestParameters.All.Single().Value.Should().Be(2);
        }

        [Fact]
        public void A_name_added_twice_is_overwritten_not_duplicated()
        {
            _connector.AddParameter(_command, "@Name", "first");
            _connector.AddParameter(_command, "@Name", "second");

            _command.TestParameters.All.Should().ContainSingle()
                .Which.Value.Should().Be("second");
        }

        [Fact]
        public void A_name_added_twice_through_the_context_is_overwritten_not_duplicated()
        {
            var context = new SqlBuilderContext(_connector);

            context.AddParameter(_command, "@Name", "first");
            context.AddParameter(_command, "@Name", "second");

            _command.TestParameters.All.Should().ContainSingle()
                .Which.Value.Should().Be("second");
        }

        [Fact]
        public void A_generated_name_added_twice_is_overwritten_not_duplicated()
        {
            var context = new SqlBuilderContext(_connector);
            var name = context.GenerateParameterName("Id", 0, _command);

            context.AddParameter(_command, name, 1);
            context.AddParameter(_command, name, 2);

            _command.TestParameters.All.Should().ContainSingle()
                .Which.Value.Should().Be(2);
        }

        [Fact]
        public void A_generated_name_added_after_another_parameter_is_looked_up()
        {
            var context = new SqlBuilderContext(_connector);
            var name = context.GenerateParameterName("Id", 0, _command);
            context.AddParameter(_command, name, 1);
            var stale = context.GenerateParameterName("Id", 0, _command);
            _connector.AddParameter(_command, "@Other", 0);

            context.AddParameter(_command, stale, 2);

            _command.TestParameters.ContainsByNameCalls.Should().Be(2, "the count moved since the name was generated");
            _command.TestParameters.All.Select(p => p.ParameterName).Should().Equal(name, "@Other", stale);
        }

        [Fact]
        public void A_name_generated_for_another_command_is_looked_up()
        {
            var context = new SqlBuilderContext(_connector);
            var other = new TestDbCommand();
            var name = context.GenerateParameterName("Id", 0, other);

            context.AddParameter(_command, name, 1);

            _command.TestParameters.ContainsByNameCalls.Should().Be(1);
            _command.TestParameters.All.Should().ContainSingle();
        }
    }
}
