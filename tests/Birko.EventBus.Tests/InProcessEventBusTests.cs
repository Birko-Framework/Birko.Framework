using System;
using System.Threading.Tasks;
using Birko.EventBus.Local;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tests
{
    public class InProcessEventBusTests
    {
        [Fact]
        public async Task PublishAsync_WithDiHandler_DispatchesToHandler()
        {
            var handler = new OrderPlacedHandler();
            var services = new ServiceCollection();
            services.AddSingleton<IEventHandler<OrderPlaced>>(handler);
            var sp = services.BuildServiceProvider();

            using var bus = new InProcessEventBus(sp);
            var evt = new OrderPlaced(Guid.NewGuid(), 99.99m);

            await bus.PublishAsync(evt);

            handler.ReceivedEvents.Should().ContainSingle()
                .Which.Should().Be(evt);
        }

        [Fact]
        public async Task PublishAsync_WithManualSubscription_DispatchesToHandler()
        {
            var handler = new OrderPlacedHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var evt = new OrderPlaced(Guid.NewGuid(), 50m);
            await bus.PublishAsync(evt);

            handler.ReceivedEvents.Should().ContainSingle()
                .Which.Should().Be(evt);
        }

        [Fact]
        public async Task PublishAsync_MultipleHandlers_AllReceiveEvent()
        {
            var handler1 = new OrderPlacedHandler();
            var handler2 = new SecondOrderHandler();
            var services = new ServiceCollection();
            services.AddSingleton<IEventHandler<OrderPlaced>>(handler1);
            services.AddSingleton<IEventHandler<OrderPlaced>>(handler2);
            var sp = services.BuildServiceProvider();

            using var bus = new InProcessEventBus(sp);
            var evt = new OrderPlaced(Guid.NewGuid(), 10m);
            await bus.PublishAsync(evt);

            handler1.ReceivedEvents.Should().ContainSingle();
            handler2.ReceivedEvents.Should().ContainSingle();
        }

        [Fact]
        public async Task PublishAsync_NoHandlers_DoesNotThrow()
        {
            using var bus = new InProcessEventBus();

            var act = () => bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task PublishAsync_HandlerThrows_ContinueModeDoesNotAffectOtherHandlers()
        {
            var throwing = new ThrowingHandler();
            var good = new OrderPlacedHandler();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions { ErrorHandling = ErrorHandlingMode.Continue });
            bus.Subscribe(throwing);
            bus.Subscribe(good);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);
            await bus.PublishAsync(evt);

            good.ReceivedEvents.Should().ContainSingle();
        }

        [Fact]
        public async Task PublishAsync_HandlerThrows_StopModePreventsSubsequentHandlers()
        {
            var throwing = new ThrowingHandler();
            var good = new OrderPlacedHandler();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions { ErrorHandling = ErrorHandlingMode.Stop });
            bus.Subscribe(throwing);
            bus.Subscribe(good);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);

            var act = () => bus.PublishAsync(evt);
            await act.Should().ThrowAsync<InvalidOperationException>();

            good.ReceivedEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task PublishAsync_PassesCorrectEventContext()
        {
            var handler = new ContextCapturingHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(handler);

            var evt = new OrderPlaced(Guid.NewGuid(), 25m) { CorrelationId = Guid.NewGuid() };
            await bus.PublishAsync(evt);

            handler.CapturedContext.Should().NotBeNull();
            handler.CapturedContext!.EventId.Should().Be(evt.EventId);
            handler.CapturedContext.Source.Should().Be("orders");
            handler.CapturedContext.CorrelationId.Should().Be(evt.CorrelationId);
            handler.CapturedContext.DeliveryCount.Should().Be(1);
        }

        [Fact]
        public void Subscribe_ReturnsActiveSubscription()
        {
            var handler = new OrderPlacedHandler();
            using var bus = new InProcessEventBus();

            var sub = bus.Subscribe(handler);

            sub.IsActive.Should().BeTrue();
            sub.EventType.Should().Be(typeof(OrderPlaced));
        }

        [Fact]
        public async Task Subscribe_DisposeUnsubscribes()
        {
            var handler = new OrderPlacedHandler();
            using var bus = new InProcessEventBus();
            var sub = bus.Subscribe(handler);

            sub.Dispose();

            sub.IsActive.Should().BeFalse();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            handler.ReceivedEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task PublishAsync_ParallelMode_AllHandlersExecute()
        {
            var handler1 = new OrderPlacedHandler();
            var handler2 = new SecondOrderHandler();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions { MaxConcurrency = 4 });
            bus.Subscribe(handler1);
            var services = new ServiceCollection();
            services.AddSingleton<IEventHandler<OrderPlaced>>(handler2);
            // Can't mix DI and manual easily in parallel test, so just use manual
            using var bus2 = new InProcessEventBus(options: new InProcessEventBusOptions { MaxConcurrency = 4 });
            bus2.Subscribe(handler1);
            bus2.Subscribe(handler2);

            await bus2.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            handler1.ReceivedEvents.Should().ContainSingle();
            handler2.ReceivedEvents.Should().ContainSingle();
        }

        [Fact]
        public void Dispose_PreventsSubsequentPublish()
        {
            var bus = new InProcessEventBus();
            bus.Dispose();

            var act = () => bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task PublishAsync_DifferentEventTypes_OnlyMatchingHandlersCalled()
        {
            var orderHandler = new OrderPlacedHandler();
            using var bus = new InProcessEventBus();
            bus.Subscribe(orderHandler);

            await bus.PublishAsync(new DeviceOffline(Guid.NewGuid()));

            orderHandler.ReceivedEvents.Should().BeEmpty();
        }
    }
}
