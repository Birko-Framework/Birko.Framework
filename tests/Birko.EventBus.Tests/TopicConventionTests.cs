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

        // CR-L251: verified — the class's public GetTopic(IEvent) DOES implicitly implement the
        // interface's default method, so an ITopicConvention-typed caller of DefaultTopicConvention
        // gets the source-aware mapping (not the type-based default). The audit's premise that the
        // interface path returns "events.*" was incorrect.
        [Fact]
        public void DefaultConvention_ViaInterfaceReference_IsSourceAware()
        {
            ITopicConvention convention = new DefaultTopicConvention();
            IEvent evt = new OrderPlaced(Guid.NewGuid(), 1m);

            convention.GetTopic(evt).Should().Be("orders.order-placed");
        }

        // CR-L251: AttributeTopicConvention now implements GetTopic(IEvent) so an attribute-less,
        // source-bearing event routes the same way it would under DefaultTopicConvention (source-prefixed),
        // instead of the interface default's type-based "events.*" that ignored Source.
        [Fact]
        public void AttributeConvention_EventBased_AttributeLessSourceEvent_IsSourceAware()
        {
            var convention = new AttributeTopicConvention();
            var evt = new OrderPlaced(Guid.NewGuid(), 1m); // no [Topic], Source = "orders"

            convention.GetTopic(evt).Should().Be("orders.order-placed");
        }

        [Fact]
        public void AttributeConvention_EventBased_WithAttribute_UsesAttributeAndIgnoresSource()
        {
            var convention = new AttributeTopicConvention();
            IEvent evt = new CustomTopicEvent(); // [Topic("custom.my-topic")], Source = "test"

            convention.GetTopic(evt).Should().Be("custom.my-topic");
        }

        [Topic("custom.my-topic")]
        private sealed record CustomTopicEvent : EventBase
        {
            public override string Source => "test";
        }
    }
}
