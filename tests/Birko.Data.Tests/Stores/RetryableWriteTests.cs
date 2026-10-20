using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores
{
    /// <summary>
    /// TASK-694: <see cref="RetryableWrite"/> reaches the <see cref="IRetryableWriteStore"/> beneath decorators, and runs
    /// the write as is when there is none. The savepoint itself is measured live (Task685InTransactionLiveTests).
    /// </summary>
    public class RetryableWriteTests
    {
        private sealed class Capable : IRetryableWriteStore
        {
            public List<string> Calls { get; } = new();

            public void RunRetryable(Action write)
            {
                Calls.Add("sync");
                write();
            }

            public async Task RunRetryableAsync(Func<Task> write, CancellationToken ct = default)
            {
                Calls.Add("async");
                await write();
            }
        }

        private sealed class Wrapper : IStoreWrapper
        {
            private readonly object? _inner;
            public Wrapper(object? inner) => _inner = inner;
            public object? GetInnerStore() => _inner;
        }

        private sealed class SelfWrapper : IStoreWrapper
        {
            public object? GetInnerStore() => this;
        }

        [Fact]
        public void The_capability_is_found_beneath_decorators()
        {
            var capable = new Capable();

            RetryableWrite.Find(new Wrapper(new Wrapper(capable))).Should().BeSameAs(capable);
            RetryableWrite.Find(capable).Should().BeSameAs(capable);
        }

        [Fact]
        public void Without_the_capability_the_write_runs_as_is()
        {
            var ran = 0;

            RetryableWrite.Run(new Wrapper(new object()), () => ran++);
            RetryableWrite.Run(null, () => ran++);

            ran.Should().Be(2);
            RetryableWrite.Find(new Wrapper(null)).Should().BeNull();
        }

        [Fact]
        public async Task Every_overload_routes_through_the_store_and_returns_the_writes_result()
        {
            var capable = new Capable();
            var store = new Wrapper(capable);

            RetryableWrite.Run(store, () => { });
            RetryableWrite.Run(store, () => 7).Should().Be(7);
            await RetryableWrite.RunAsync(store, () => Task.CompletedTask);
            (await RetryableWrite.RunAsync(store, () => Task.FromResult("guid"))).Should().Be("guid");

            capable.Calls.Should().Equal("sync", "sync", "async", "async");
        }

        [Fact]
        public async Task A_failing_write_still_throws_its_own_exception()
        {
            var store = new Capable();

            FluentActions.Invoking(() => RetryableWrite.Run(store, () => throw new InvalidOperationException("refused")))
                .Should().Throw<InvalidOperationException>().WithMessage("refused");
            await FluentActions.Awaiting(() => RetryableWrite.RunAsync(store, () => Task.FromException(new InvalidOperationException("refused"))))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("refused");
        }

        [Fact]
        public void A_wrapper_that_returns_itself_does_not_loop()
        {
            var ran = false;

            RetryableWrite.Run(new SelfWrapper(), () => ran = true);

            ran.Should().BeTrue();
        }
    }
}
