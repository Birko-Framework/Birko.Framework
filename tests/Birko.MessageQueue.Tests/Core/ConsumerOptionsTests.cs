using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    public class ConsumerOptionsTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var options = new ConsumerOptions();

            options.AckMode.Should().Be(MessageAckMode.AutoAck);
            options.PrefetchCount.Should().Be(1);
            options.GroupId.Should().BeNull();
            options.FromBeginning.Should().BeFalse();
        }
    }
}
