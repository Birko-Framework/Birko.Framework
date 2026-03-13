using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    public class MessageContextTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            var message = new QueueMessage { Body = "test" };
            var consumer = new Birko.MessageQueue.InMemory.InMemoryConsumer(
                new Birko.MessageQueue.InMemory.InMemoryChannel(),
                new Birko.MessageQueue.Serialization.JsonMessageSerializer());

            var context = new MessageContext(message, "orders.created", consumer);

            context.Message.Should().BeSameAs(message);
            context.Destination.Should().Be("orders.created");
            context.Consumer.Should().BeSameAs(consumer);
            context.DeliveryCount.Should().Be(1);
        }

        [Fact]
        public void Constructor_ThrowsOnNullMessage()
        {
            var consumer = new Birko.MessageQueue.InMemory.InMemoryConsumer(
                new Birko.MessageQueue.InMemory.InMemoryChannel(),
                new Birko.MessageQueue.Serialization.JsonMessageSerializer());

            var act = () => new MessageContext(null!, "dest", consumer);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_ThrowsOnNullDestination()
        {
            var message = new QueueMessage();
            var consumer = new Birko.MessageQueue.InMemory.InMemoryConsumer(
                new Birko.MessageQueue.InMemory.InMemoryChannel(),
                new Birko.MessageQueue.Serialization.JsonMessageSerializer());

            var act = () => new MessageContext(message, null!, consumer);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
