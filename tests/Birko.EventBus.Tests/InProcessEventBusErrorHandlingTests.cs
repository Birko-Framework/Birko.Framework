using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Local;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    /// <summary>
    /// CR-M185: the Continue/Stop dispatch catch blocks had empty bodies and there was no logging hook,
    /// so a throwing handler vanished with zero diagnostics. An optional OnHandlerError callback now fires.
    /// CR-M184: in parallel dispatch (MaxConcurrency &gt; 1), Stop mode did not actually halt the other
    /// handlers — they ran to completion. A first-failure cancellation now aborts the remaining handlers,
    /// and the original exception (not a follow-on cancellation) is surfaced.
    /// </summary>
    public class InProcessEventBusErrorHandlingTests
    {
        /// <summary>Waits on the (linked) token, then records completion — cancelled work leaves Completed false.</summary>
        private sealed class TokenAwareHandler : IEventHandler<OrderPlaced>
        {
            public bool Completed { get; private set; }
            public async Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                Completed = true;
            }
        }

        // ---- CR-M185 ----

        [Fact]
        public async Task ContinueMode_InvokesOnHandlerError_WithEventAndException()
        {
            var captured = new List<(IEvent evt, Exception ex)>();
            var good = new OrderPlacedHandler();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions
            {
                ErrorHandling = ErrorHandlingMode.Continue,
                OnHandlerError = (e, ex) => captured.Add((e, ex)),
            });
            bus.Subscribe(new ThrowingHandler());
            bus.Subscribe(good);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);
            await bus.PublishAsync(evt);

            captured.Should().ContainSingle();
            captured[0].evt.Should().Be(evt);
            captured[0].ex.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("Handler failed");
            good.ReceivedEvents.Should().ContainSingle("Continue mode still runs the remaining handlers");
        }

        [Fact]
        public async Task StopMode_InvokesOnHandlerError_BeforePropagating()
        {
            Exception? captured = null;
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions
            {
                ErrorHandling = ErrorHandlingMode.Stop,
                OnHandlerError = (_, ex) => captured = ex,
            });
            bus.Subscribe(new ThrowingHandler());

            await bus.Invoking(b => b.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m)))
                .Should().ThrowAsync<InvalidOperationException>();

            captured.Should().BeOfType<InvalidOperationException>("the error is reported before it propagates (CR-M185)");
        }

        // ---- CR-M184 ----

        [Fact]
        public async Task ParallelStopMode_HaltsRemainingHandlers_AndSurfacesOriginalError()
        {
            var tokenAware = new TokenAwareHandler();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions
            {
                MaxConcurrency = 4,
                ErrorHandling = ErrorHandlingMode.Stop,
            });
            bus.Subscribe(new ThrowingHandler()); // throws immediately → triggers Stop cancellation
            bus.Subscribe(tokenAware);            // observes the token → must be cancelled, not completed

            await bus.Invoking(b => b.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m)))
                .Should().ThrowAsync<InvalidOperationException>("the original handler failure is surfaced, not the follow-on cancellation (CR-M184)");

            tokenAware.Completed.Should().BeFalse("Stop mode cancels the remaining parallel handlers instead of letting them finish");
        }

        [Fact]
        public async Task ParallelContinueMode_ReportsError_AndRunsOtherHandlers()
        {
            var captured = 0;
            var good = new OrderPlacedHandler();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions
            {
                MaxConcurrency = 4,
                ErrorHandling = ErrorHandlingMode.Continue,
                OnHandlerError = (_, _) => Interlocked.Increment(ref captured),
            });
            bus.Subscribe(new ThrowingHandler());
            bus.Subscribe(good);

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            captured.Should().Be(1);
            good.ReceivedEvents.Should().ContainSingle();
        }
    }
}
