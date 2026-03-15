using System;
using Birko.EventBus.Routing;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    public class TopicConventionTests
    {
        [Fact]
        public void DefaultConvention_TypeBased_ReturnsKebabCase()
        {
            var convention = new DefaultTopicConvention();

            var topic = convention.GetTopic(typeof(OrderPlaced));

            topic.Should().Be("events.order-placed");
        }

        [Fact]
        public void DefaultConvention_EventBased_UsesSource()
        {
            var convention = new DefaultTopicConvention();
            var evt = new OrderPlaced(Guid.NewGuid(), 1m);

            var topic = convention.GetTopic(evt);

            topic.Should().Be("orders.order-placed");
        }

        [Fact]
        public void AttributeConvention_WithAttribute_UsesAttributeValue()
        {
            var convention = new AttributeTopicConvention();

            var topic = convention.GetTopic(typeof(CustomTopicEvent));

            topic.Should().Be("custom.my-topic");
        }

        [Fact]
        public void AttributeConvention_WithoutAttribute_FallsBackToDefault()
        {
            var convention = new AttributeTopicConvention();

            var topic = convention.GetTopic(typeof(OrderPlaced));

            topic.Should().Be("events.order-placed");
        }

        [Topic("custom.my-topic")]
        private sealed record CustomTopicEvent : EventBase
        {
            public override string Source => "test";
        }
    }
}
