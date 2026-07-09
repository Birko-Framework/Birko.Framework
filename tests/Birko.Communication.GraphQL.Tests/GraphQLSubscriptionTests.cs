using Birko.Communication.GraphQL;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLSubscriptionTests
{
    [Fact]
    public void Subscribe_ReturnsDisposable()
    {
        using var cts = new CancellationTokenSource();
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        using var subscription = new GraphQLSubscription<string>(ws, "sub_1", cts);

        var observable = subscription.AsObservable();
        var unsubscribe = observable.Subscribe(new TestObserver<string>());

        unsubscribe.Should().NotBeNull();
        unsubscribe.Dispose();
    }

    [Fact]
    public void Subscribe_CallsOnCompleted_OnDispose()
    {
        using var cts = new CancellationTokenSource();
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        var subscription = new GraphQLSubscription<string>(ws, "sub_1", cts);

        var observer = new TestObserver<string>();
        subscription.AsObservable().Subscribe(observer);

        subscription.Dispose();

        observer.Completed.Should().BeTrue();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var cts = new CancellationTokenSource();
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        var subscription = new GraphQLSubscription<string>(ws, "sub_1", cts);

        var observer = new TestObserver<string>();
        subscription.AsObservable().Subscribe(observer);

        subscription.Dispose();
        subscription.Dispose();

        observer.Completed.Should().BeTrue();
        // No exception on second dispose
    }

    [Fact]
    public async Task Unsubscribe_OnDisposedSubscription_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        var subscription = new GraphQLSubscription<string>(ws, "sub_1", cts);

        subscription.Dispose();

        // Should not throw — WebSocket is already closed/disposed
        Func<Task> act = () => subscription.UnsubscribeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void AsObservable_ReturnsSameInstance()
    {
        using var cts = new CancellationTokenSource();
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        using var subscription = new GraphQLSubscription<string>(ws, "sub_1", cts);

        var obs1 = subscription.AsObservable();
        var obs2 = subscription.AsObservable();

        obs1.Should().BeSameAs(obs2);
    }

    // --- Message dispatch (CR-M046): HandleMessageAsync parses next/complete/error frames and the
    //     id-filter, dispatching to observers — exercised without a live WebSocket. ---

    private static (GraphQLSubscription<string> sub, TestObserver<string> obs, CancellationTokenSource cts) NewSub(string id = "sub_1")
    {
        var cts = new CancellationTokenSource();
        var ws = new System.Net.WebSockets.ClientWebSocket();
        var sub = new GraphQLSubscription<string>(ws, id, cts);
        var obs = new TestObserver<string>();
        sub.AsObservable().Subscribe(obs);
        return (sub, obs, cts);
    }

    [Fact]
    public async Task HandleMessage_NextFrame_DispatchesOnNext()
    {
        var (sub, obs, cts) = NewSub();
        using (cts)
        {
            var stop = await sub.HandleMessageAsync("""{"type":"next","id":"sub_1","payload":{"data":"hello"}}""");

            stop.Should().BeFalse("a data frame does not terminate the subscription");
            obs.ReceivedValues.Should().Equal("hello");
        }
    }

    [Fact]
    public async Task HandleMessage_WrongId_IsIgnored()
    {
        var (sub, obs, cts) = NewSub("sub_1");
        using (cts)
        {
            var stop = await sub.HandleMessageAsync("""{"type":"next","id":"other","payload":{"data":"nope"}}""");

            stop.Should().BeFalse();
            obs.ReceivedValues.Should().BeEmpty("frames for another subscription id are filtered out");
        }
    }

    [Fact]
    public async Task HandleMessage_CompleteFrame_CompletesAndStops()
    {
        var (sub, obs, cts) = NewSub();
        using (cts)
        {
            var stop = await sub.HandleMessageAsync("""{"type":"complete","id":"sub_1"}""");

            stop.Should().BeTrue("complete terminates the receive loop");
            obs.Completed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task HandleMessage_ErrorFrame_DispatchesOnErrorAndStops()
    {
        var (sub, obs, cts) = NewSub();
        using (cts)
        {
            var stop = await sub.HandleMessageAsync("""{"type":"error","id":"sub_1","payload":[{"message":"boom"}]}""");

            stop.Should().BeTrue();
            obs.Error.Should().BeOfType<GraphQLException>();
            ((GraphQLException)obs.Error!).Errors.Should().ContainSingle(e => e.Message == "boom");
        }
    }

    [Fact]
    public async Task HandleMessage_NextFrameWithPayloadErrors_DispatchesOnError()
    {
        var (sub, obs, cts) = NewSub();
        using (cts)
        {
            var stop = await sub.HandleMessageAsync("""{"type":"next","id":"sub_1","payload":{"errors":[{"message":"bad field"}]}}""");

            stop.Should().BeFalse();
            obs.Error.Should().BeOfType<GraphQLException>();
        }
    }

    private class TestObserver<T> : IObserver<T>
    {
        public List<T> ReceivedValues { get; } = [];
        public bool Completed { get; private set; }
        public Exception? Error { get; private set; }

        public void OnNext(T value) => ReceivedValues.Add(value);
        public void OnError(Exception error) => Error = error;
        public void OnCompleted() => Completed = true;
    }
}
