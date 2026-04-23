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
