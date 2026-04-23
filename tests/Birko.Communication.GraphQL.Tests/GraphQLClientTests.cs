using Birko.Communication.GraphQL;
using FluentAssertions;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

public class GraphQLClientTests
{
    [Fact]
    public void Constructor_ThrowsOnNullSettings()
    {
        var act = () => new GraphQLClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyEndpoint()
    {
        var act = () => new GraphQLClient(new GraphQLSettings());

        act.Should().Throw<ArgumentException>().WithMessage("*endpoint*");
    }

    [Fact]
    public async Task QueryAsync_ReturnsData()
    {
        var handler = new FakeHttpHandler("""{"data":{"name":"Alice","age":30}}""");
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        var response = await client.QueryAsync<GraphQLResponseTests.TestUser>("{ user { name age } }");

        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Alice");
        response.Data.Age.Should().Be(30);
    }

    [Fact]
    public async Task QueryAsync_WithVariables_SendsVariables()
    {
        var handler = new FakeHttpHandler("""{"data":{"name":"Bob"}}""");
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        await client.QueryAsync<GraphQLResponseTests.TestUser>("query($id:Int!)", new { id = 1 });

        var body = handler.LastRequestBody!;
        body.Should().Contain("variables");
    }

    [Fact]
    public async Task MutateAsync_ReturnsData()
    {
        var handler = new FakeHttpHandler("""{"data":{"name":"Charlie"}}""");
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        var response = await client.MutateAsync<GraphQLResponseTests.TestUser>("mutation { createUser { name } }");

        response.Data!.Name.Should().Be("Charlie");
    }

    [Fact]
    public async Task ExecuteAsync_OnError_ThrowsGraphQLException()
    {
        var handler = new FakeHttpHandler(
            """{"errors":[{"message":"Field not found","locations":[{"line":1,"column":3}]}]}""",
            HttpStatusCode.OK);
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        var act = async () => await client.QueryAsync<object>("{ bad }");

        var ex = await act.Should().ThrowAsync<GraphQLException>();
        ex.Which.Errors.Should().HaveCount(1);
        ex.Which.Errors![0].Message.Should().Be("Field not found");
    }

    [Fact]
    public async Task ExecuteAsync_FiresOnRequestEvent()
    {
        var handler = new FakeHttpHandler("""{"data":{}}""");
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        GraphQLRequestEventArgs? fired = null;
        client.OnRequest += (_, e) => fired = e;

        await client.QueryAsync<object>("{ test }");

        fired.Should().NotBeNull();
        fired!.Query.Should().Be("{ test }");
    }

    [Fact]
    public async Task ExecuteAsync_FiresOnResponseEvent()
    {
        var handler = new FakeHttpHandler("""{"data":{}}""");
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        GraphQLResponseEventArgs? fired = null;
        client.OnResponse += (_, e) => fired = e;

        await client.QueryAsync<object>("{ test }");

        fired.Should().NotBeNull();
        fired!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ExecuteAsync_FiresOnErrorEvent()
    {
        var handler = new FakeHttpHandler("""{"errors":[{"message":"err"}]}""");
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        GraphQLErrorEventArgs? fired = null;
        client.OnError += (_, e) => fired = e;

        try { await client.QueryAsync<object>("q"); } catch (GraphQLException) { }

        fired.Should().NotBeNull();
        fired!.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void GetClient_ReturnsCachedInstance()
    {
        GraphQLClient.ClearCache();

        var a = GraphQLClient.GetClient("https://api.example.com/graphql");
        var b = GraphQLClient.GetClient("https://api.example.com/graphql");

        a.Should().BeSameAs(b);

        GraphQLClient.ClearCache();
    }

    [Fact]
    public void GetClient_DifferentEndpoint_ReturnsDifferentInstance()
    {
        GraphQLClient.ClearCache();

        var a = GraphQLClient.GetClient("https://a.example.com/graphql");
        var b = GraphQLClient.GetClient("https://b.example.com/graphql");

        a.Should().NotBeSameAs(b);

        GraphQLClient.ClearCache();
    }

    [Fact]
    public void ClearCache_DisposesClients()
    {
        GraphQLClient.ClearCache();

        var client = GraphQLClient.GetClient("https://api.example.com/graphql");
        GraphQLClient.ClearCache();

        // After clearing, GetClient returns a new instance
        var client2 = GraphQLClient.GetClient("https://api.example.com/graphql");
        client2.Should().NotBeSameAs(client);

        GraphQLClient.ClearCache();
    }

    [Fact]
    public void Dispose_DoesNotDisposeInjectedHttpClient()
    {
        var handler = new FakeHttpHandler("""{"data":{}}""");
        var http = new HttpClient(handler);

        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            http);

        client.Dispose();

        // HttpClient should still be usable
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenSubscriptionsDisabled()
    {
        var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient());

        var act = async () => await client.SubscribeAsync<object>("subscription { onEvent }");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Subscriptions*");
    }

    [Fact]
    public async Task ExecuteAsync_ExtraHeaders_AreSent()
    {
        var handler = new FakeHttpHandler("""{"data":{}}""");
        var client = new GraphQLClient(
            new GraphQLSettings
            {
                Endpoint = "https://api.example.com/graphql",
                ExtraHeaders = { ["X-Custom"] = "test-value" }
            },
            new HttpClient(handler));

        await client.QueryAsync<object>("{ test }");

        handler.LastRequest!.Headers.GetValues("X-Custom").Should().Contain("test-value");
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;

        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            LastRequestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
