# Birko.Communication.GraphQL.Tests

## Overview
Unit tests for Birko.Communication.GraphQL covering settings, request/response serialization, client operations, exception handling, and request builder.

## Project Location
`tests/Birko.Communication.GraphQL.Tests/`

## Test Classes

### GraphQLSettingsTests.cs
Tests for GraphQLSettings defaults, Endpoint/Location alias mapping, RemoteSettings inheritance, ExtraHeaders initialization.

### GraphQLExceptionTests.cs
Tests for all four GraphQLException constructors and property assignments.

### GraphQLRequestTests.cs
Tests for GraphQLRequest construction, Serialize() JSON output, null variables handling, dictionary precedence, extensions.

### GraphQLResponseTests.cs
Tests for GraphQLResponse<T> Deserialize: success data, errors with locations, null data, extensions, path.

### GraphQLRequestBuilderTests.cs
Tests for fluent builder: Query/Mutation setting, Variables, OperationName, Build validation (no query throws, both throws), WithExtension, chaining.

### GraphQLClientTests.cs
Tests for GraphQLClient: constructor validation, QueryAsync/MutateAsync with FakeHttpHandler, error handling, events (OnRequest/OnResponse/OnError), static caching (GetClient/ClearCache/RemoveClient), Dispose ownership, subscriptions disabled check, extra headers.

### GraphQLSubscriptionTests.cs
Tests for subscription lifecycle: Subscribe returns disposable, OnCompleted on dispose, idempotent dispose, UnsubscribeAsync safety, AsObservable identity.

## Dependencies
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Birko.Communication.GraphQL (shared project import)
- Birko.Serialization (shared project import)
- Birko.Configuration (shared project import)
- Birko.Contracts (shared project import)
