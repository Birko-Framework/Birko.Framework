using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    public class QueueMessageTests
    {
        [Fact]
        public void Constructor_SetsDefaults()
        {
            var message = new QueueMessage();

            message.Id.Should().NotBeEmpty();
            message.Body.Should().BeEmpty();
            message.PayloadType.Should().BeNull();
            message.Headers.Should().NotBeNull();
            message.Priority.Should().Be(0);
            message.Delay.Should().BeNull();
            message.TimeToLive.Should().BeNull();
        }

        [Fact]
        public void TwoMessages_HaveDifferentIds()
        {
            var m1 = new QueueMessage();
            var m2 = new QueueMessage();

            m1.Id.Should().NotBe(m2.Id);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var message = new QueueMessage
            {
                Body = "{\"test\":1}",
                PayloadType = "MyType",
                Priority = 5,
                Delay = TimeSpan.FromSeconds(10),
                TimeToLive = TimeSpan.FromMinutes(5)
            };

            message.Body.Should().Be("{\"test\":1}");
            message.PayloadType.Should().Be("MyType");
            message.Priority.Should().Be(5);
            message.Delay.Should().Be(TimeSpan.FromSeconds(10));
            message.TimeToLive.Should().Be(TimeSpan.FromMinutes(5));
        }
    }
}
