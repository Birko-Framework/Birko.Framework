# Birko.Communication.GraphQL

## Overview
GraphQL client library providing zero-dependency query, mutation, and subscription support with optional OAuth2 Bearer token injection.

## Project Location
`C:\Source\Birko.Communication.GraphQL\`

## Components

### GraphQLSettings.cs
- **GraphQLSubscriptionProtocol** — Enum: WebSocket, SSE
- **GraphQLSettings** — Extends `RemoteSettings` (Birko.Configuration). Endpoint = Location alias. Adds SchemaPath ("/graphql"), UseSubscriptions (false), SubscriptionProtocol (WebSocket), TimeoutSeconds (30), EnableAutoPersistedQueries (false), ExtraHeaders (dict)

### GraphQLError.cs
- **GraphQLError** — Message, Locations (list of GraphQLLocation), Path (list of strings), Extensions (dict)
- **GraphQLLocation** — Line, Column

### GraphQLRequest.cs
- **GraphQLRequest** — Query, Variables (object?), VariablesDictionary (dict?), OperationName, Extensions (dict?). Serialize(ISerializer) produces camelCase JSON.

### GraphQLResponse.cs
- **GraphQLResponse<T>** — Data (T?), Errors (list?), Extensions (dict?). HasErrors property. Static Deserialize(string, ISerializer) factory.

### GraphQLException.cs
- **GraphQLException** — Exception with Errors (IReadOnlyList), StatusCode (int?). Four constructor overloads.

### IGraphQLClient.cs
- **IGraphQLClient** — Interface: QueryAsync<T>, MutateAsync<T>, SubscribeAsync<T>, ExecuteAsync<T>. Events: OnRequest, OnResponse, OnError (with custom EventArgs).

### GraphQLClient.cs
- **GraphQLClient** — Full implementation of IGraphQLClient. Uses HttpClient for queries/mutations, ClientWebSocket for subscriptions. Static GetClient(string) caching (RestClient pattern). Uses SystemJsonSerializer from Birko.Serialization. Optional HttpClient injection. SemaphoreSlim for thread-safe request serialization.

### GraphQLSubscription.cs
- **IGraphQLSubscription<T>** — AsObservable(), UnsubscribeAsync(), IDisposable
- **GraphQLSubscription<T>** — IObservable<T> over ClientWebSocket. graphql-ws protocol (connection_init → connection_ack → start/stop/data/complete). Background receive loop.

### GraphQLRequestBuilder.cs
- **GraphQLRequestBuilder** — Fluent API: Query(), Mutation(), Variables(), OperationName(), WithExtension(), Build()

## Dependencies
- **Birko.Configuration** — RemoteSettings base class
- **Birko.Serialization** — ISerializer for JSON serialization (SystemJsonSerializer)
- **System.Net.Http** — HttpClient for queries/mutations
- **System.Net.WebSockets** — ClientWebSocket for subscriptions
- **System.Text.Json** — JSON parsing (via Birko.Serialization)

## Patterns
- Static GetClient caching: Dictionary<string, GraphQLClient> with SemaphoreSlim (RestClient pattern)
- Settings hierarchy: GraphQLSettings extends RemoteSettings, Endpoint = Location alias
- HttpClient ownership: optional injection, disposes only if created internally
- ISerializer integration: uses Birko.Serialization.ISerializer (SystemJsonSerializer) instead of raw System.Text.Json
- graphql-ws protocol: connection_init → connection_ack → start/stop/data/complete for subscriptions
- Thread-safe operations: SemaphoreSlim for request serialization and subscription management

## Maintenance
- Endpoint URL uses Location property from RemoteSettings
- When adding new features, update IGraphQLClient interface and GraphQLClient implementation
- Subscriptions use graphql-transport-ws sub-protocol (Apollo subscriptions standard)
