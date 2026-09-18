using Birko.AI.Agents;
using Birko.AI.Factories;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Agents.Tests
{
    /// <summary>
    /// Covers the untested AgentRegistration surface flagged by CR-M005 — the coding-agent
    /// membership check and the factory registration/alias resolution.
    /// </summary>
    public class AgentRegistrationTests
    {
        [Theory]
        [InlineData("coding")]
        [InlineData("csharp")]
        [InlineData("python")]
        [InlineData("typescript")]
        [InlineData("svg")]
        [InlineData("CSharp")] // case-insensitive
        public void IsCodingAgent_True_ForCodingTypes(string type)
            => AgentRegistration.IsCodingAgent(type).Should().BeTrue();

        [Theory]
        [InlineData("media")]
        [InlineData("documentation")]
        [InlineData("image")]
        [InlineData("unknown")]
        [InlineData("")]
        public void IsCodingAgent_False_ForNonCodingTypes(string type)
            => AgentRegistration.IsCodingAgent(type).Should().BeFalse();

        [Fact]
        public void RegisterAll_RegistersCoreAgentsAndAliases()
        {
            AgentRegistration.RegisterAll(); // idempotent

            AgentFactory.IsRegistered("coding").Should().BeTrue();
            AgentFactory.IsRegistered("documentation").Should().BeTrue();

            // Aliases resolve to their primary type.
            AgentFactory.ResolveAgentType("general").Should().Be("coding");
            AgentFactory.ResolveAgentType("docs").Should().Be("documentation");
            AgentFactory.ResolveAgentType("testing").Should().Be("test");
        }
    }
}
