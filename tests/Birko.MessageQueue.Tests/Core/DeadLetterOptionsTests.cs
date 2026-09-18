using Birko.MessageQueue.Retry;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    /// <summary>
    /// CR-L281: GetDeadLetterDestination (suffix-based vs explicit Destination override) was untested.
    /// </summary>
    public class DeadLetterOptionsTests
    {
        [Fact]
        public void GetDeadLetterDestination_DefaultSuffix_AppendsDlq()
        {
            var options = new DeadLetterOptions();

            options.GetDeadLetterDestination("orders").Should().Be("orders.dlq");
        }

        [Fact]
        public void GetDeadLetterDestination_CustomSuffix_IsUsed()
        {
            var options = new DeadLetterOptions { Suffix = ".dead" };

            options.GetDeadLetterDestination("orders").Should().Be("orders.dead");
        }

        [Fact]
        public void GetDeadLetterDestination_ExplicitDestination_OverridesSuffix()
        {
            var options = new DeadLetterOptions { Destination = "global-dlq" };

            options.GetDeadLetterDestination("orders").Should().Be("global-dlq");
        }

        [Fact]
        public void Defaults_AreEnabledWithDlqSuffix()
        {
            var options = new DeadLetterOptions();

            options.Enabled.Should().BeTrue();
            options.Suffix.Should().Be(".dlq");
            options.Destination.Should().BeNull();
        }
    }
}
