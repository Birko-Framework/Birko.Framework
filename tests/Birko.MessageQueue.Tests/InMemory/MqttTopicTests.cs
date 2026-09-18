using Birko.MessageQueue.Mqtt;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.InMemory
{
    public class MqttTopicTests
    {
        [Theory]
        [InlineData("sensors/temp", true)]
        [InlineData("a/b/c", true)]
        [InlineData("single", true)]
        [InlineData("", false)]
        [InlineData("sensors/+/temp", false)]
        [InlineData("sensors/#", false)]
        public void IsValidPublishTopic(string topic, bool expected)
        {
            MqttTopic.IsValidPublishTopic(topic).Should().Be(expected);
        }

        [Theory]
        [InlineData("sensors/temp", true)]
        [InlineData("sensors/+/temp", true)]
        [InlineData("sensors/#", true)]
        [InlineData("+/temp", true)]
        [InlineData("#", true)]
        [InlineData("", false)]
        [InlineData("sensors/ab+cd", false)]  // + must be alone
        [InlineData("sensors/#/more", false)]  // # must be last
        public void IsValidSubscribeFilter(string filter, bool expected)
        {
            MqttTopic.IsValidSubscribeFilter(filter).Should().Be(expected);
        }

        [Theory]
        [InlineData("sensors/+/temp", "sensors/room1/temp", true)]
        [InlineData("sensors/+/temp", "sensors/room2/temp", true)]
        [InlineData("sensors/+/temp", "sensors/room1/humidity", false)]
        [InlineData("sensors/#", "sensors/room1/temp", true)]
        [InlineData("sensors/#", "sensors", true)]  // # matches parent level per MQTT spec 4.7.1.2
        [InlineData("#", "anything/at/all", true)]
        [InlineData("exact/match", "exact/match", true)]
        [InlineData("exact/match", "exact/other", false)]
        [InlineData("+", "single", true)]
        [InlineData("+/+", "a/b", true)]
        [InlineData("a/b", "a/b/c", false)]
        public void Matches(string filter, string topic, bool expected)
        {
            MqttTopic.Matches(filter, topic).Should().Be(expected);
        }
    }
}
