using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// SH-M425 (TASK-324) — <c>AbstractAsyncStore</c>'s one-time initialization ran under the cancellation token of
/// whichever concurrent caller won the init lock. A caller with a short timeout arriving first could abort the shared
/// setup mid-flight (on a real backend, DDL half-applied), and the next caller repeated the whole init. Init now runs
/// under <see cref="CancellationToken.None"/> on the winner's own flow; the winner's cancellation is observed after it,
/// and a caller still queued for the lock stops waiting.
///
/// <para>Observed state, never "did not throw": how many times init ran, whether it ran to completion, and what the
/// init code was handed.</para>
/// </summary>
public class SharedInitCancellationTests
{
    /// <summary>An in-memory store whose init blocks on a gate and honours the token it is given, as DDL would.</summary>
    private sealed class GatedInitStore : AsyncInMemoryStore<TestModel>
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;
        public int Completed;
        public bool TokenCouldCancel;

        protected override async Task InitCoreAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref Started);
            TokenCouldCancel = ct.CanBeCanceled;
            Entered.TrySetResult();
            await Gate.Task.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Increment(ref Completed);
        }
    }

    [Fact]
    public async Task A_caller_cancelled_mid_init_does_not_abort_the_shared_initialization()
    {
        var store = new GatedInitStore();
        using var shortTimeout = new CancellationTokenSource();

        var first = store.InitAsync(shortTimeout.Token);
        await store.Entered.Task;
        shortTimeout.Cancel();
        store.Gate.SetResult();
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>(
            "the cancelled caller still learns it was cancelled — after the shared work, not in the middle of it");

        store.Completed.Should().Be(1, "the setup ran to the end although the caller that started it gave up");
    }

    [Fact]
    public async Task The_next_caller_does_not_repeat_an_initialization_a_cancelled_caller_started()
    {
        var store = new GatedInitStore();
        using var shortTimeout = new CancellationTokenSource();

        var first = store.InitAsync(shortTimeout.Token);
        await store.Entered.Task;
        var second = store.InitAsync(CancellationToken.None);
        shortTimeout.Cancel();
        store.Gate.SetResult();
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
        await second;

        store.Started.Should().Be(1, "one shared initialization, not one per caller");
        store.Completed.Should().Be(1);
    }

    [Fact]
    public async Task Init_code_is_handed_a_token_no_caller_can_cancel()
    {
        var store = new GatedInitStore();
        store.Gate.SetResult();
        using var cts = new CancellationTokenSource();

        await store.InitAsync(cts.Token);

        store.TokenCouldCancel.Should().BeFalse("one caller's token must not govern setup every caller shares");
    }

    [Fact]
    public async Task A_caller_queued_behind_the_init_can_still_stop_waiting()
    {
        // Contract pin: cancellation of the WAIT for the lock was never the defect, and is kept.
        var store = new GatedInitStore();
        var first = store.InitAsync(CancellationToken.None);
        await store.Entered.Task;
        using var impatient = new CancellationTokenSource();

        var queued = store.InitAsync(impatient.Token);
        impatient.Cancel();

        await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();
        store.Gate.SetResult();
        await first;
        store.Started.Should().Be(1);
    }

    [Fact]
    public async Task An_already_cancelled_caller_starts_no_initialization()
    {
        // Contract pin: the up-front check on every operation.
        var store = new GatedInitStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await FluentActions.Awaiting(() => store.InitAsync(cts.Token)).Should().ThrowAsync<OperationCanceledException>();

        store.Started.Should().Be(0);
    }
}
