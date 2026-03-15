using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Pipeline;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    public class EventPipelineTests
    {
        [Fact]
        public async Task ExecuteAsync_NoBehaviors_ExecutesHandler()
        {
            var pipeline = new EventPipeline([]);
            var executed = false;

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);
            var ctx = EventContext.From(evt);

            await pipeline.ExecuteAsync(evt, ctx, () => { executed = true; return Task.CompletedTask; });

            executed.Should().BeTrue();
        }

        [Fact]
        public async Task ExecuteAsync_SingleBehavior_WrapsHandler()
        {
            var order = new List<string>();
            var behavior = new TrackingBehavior("B1", order);
            var pipeline = new EventPipeline([behavior]);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);
            var ctx = EventContext.From(evt);

            await pipeline.ExecuteAsync(evt, ctx, () => { order.Add("handler"); return Task.CompletedTask; });

            order.Should().Equal("B1-before", "handler", "B1-after");
        }

        [Fact]
        public async Task ExecuteAsync_MultipleBehaviors_ExecuteInOrder()
        {
            var order = new List<string>();
            var b1 = new TrackingBehavior("B1", order);
            var b2 = new TrackingBehavior("B2", order);
            var pipeline = new EventPipeline([b1, b2]);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);
            var ctx = EventContext.From(evt);

            await pipeline.ExecuteAsync(evt, ctx, () => { order.Add("handler"); return Task.CompletedTask; });

            order.Should().Equal("B1-before", "B2-before", "handler", "B2-after", "B1-after");
        }

        private class TrackingBehavior : IEventPipelineBehavior
        {
            private readonly string _name;
            private readonly List<string> _order;

            public TrackingBehavior(string name, List<string> order)
            {
                _name = name;
                _order = order;
            }

            public async Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken cancellationToken = default)
            {
                _order.Add($"{_name}-before");
                await next();
                _order.Add($"{_name}-after");
            }
        }
    }
}
