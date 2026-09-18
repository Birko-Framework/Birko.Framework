# Birko.Communication.GraphQL

GraphQL client library for Birko Framework providing zero-dependency query, mutation, and subscription support.

## Features

- **Queries and Mutations** — Execute GraphQL operations via HTTP POST with typed responses
- **Subscriptions** — Real-time data via WebSocket using the graphql-ws protocol
- **OAuth2 Integration** — Optional Bearer token injection via Birko.Communication.OAuth
- **Static Client Caching** — `GraphQLClient.GetClient(endpoint)` with automatic caching per URL
- **Fluent Request Builder** — `GraphQLRequestBuilder` for constructing requests
- **Event-Driven** — `OnRequest`, `OnResponse`, `OnError` events for logging and monitoring
- **Settings Chain** — `GraphQLSettings` extends `RemoteSettings` from Birko.Configuration
- **Birko.Serialization** — Uses `ISerializer` (SystemJsonSerializer) for all JSON operations

## Usage

### Basic Query

```csharp
var client = new GraphQLClient(new GraphQLSettings
{
    Endpoint = "https://api.example.com/graphql"
});

var response = await client.QueryAsync<User>("{ user(id: 1) { name email } }");
Console.WriteLine(response.Data?.Name);
```

### Query with Variables

```csharp
var response = await client.QueryAsync<User>(
    "query GetUser($id: Int!) { user(id: $id) { name } }",
    new { id = 1 },
    "GetUser");
```

### Mutation

```csharp
var response = await client.MutateAsync<User>(
    "mutation CreateUser($name: String!) { createUser(name: $name) { id name } }",
    new { name = "Alice" });
```

### Request Builder

```csharp
var request = new GraphQLRequestBuilder()
    .Query("query($id: Int!) { user(id: $id) { name } }")
    .Variables(new { id = 1 })
    .OperationName("GetUser")
    .Build();

var response = await client.ExecuteAsync<User>(request);
```

### Static Client Caching

```csharp
var client = GraphQLClient.GetClient("https://api.example.com/graphql");
// Same endpoint returns same instance
GraphQLClient.ClearCache();
```

### Subscription

```csharp
var client = new GraphQLClient(new GraphQLSettings
{
    Endpoint = "wss://api.example.com/graphql",
    UseSubscriptions = true
});

var subscription = await client.SubscribeAsync<string>(
    "subscription { onMessage { text } }");

subscription.AsObservable().Subscribe(message =>
{
    Console.WriteLine(message);
});
```

## Dependencies

- Birko.Configuration (RemoteSettings)
- Birko.Serialization (ISerializer / SystemJsonSerializer)
- System.Net.Http (BCL)
- System.Net.WebSockets (BCL)

No external NuGet packages.

## License

See [License.md](License.md)
