using System;
using System.Collections.Generic;
using Birko.AI.Agents;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Agents.Tests
{
    /// <summary>
    /// TASK-715. Measured before the fix: <c>AgentRegistration.Create("zai", …)</c> wrote <c>useCodingEndpoint</c> into the
    /// caller's own config dictionary, so a dictionary reused for a second agent carried the first one's coding hint.
    /// </summary>
    public class Task715AgentRegistrationTests
    {
        [Fact]
        public void Creating_a_Z_AI_coding_agent_leaves_the_caller_s_config_as_it_was()
        {
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["apiKey"] = "test-key" };

            AgentRegistration.Create("zai", config: config, agentType: "coding");

            config.Should().ContainSingle().Which.Key.Should().Be("apiKey");
        }
    }
}
