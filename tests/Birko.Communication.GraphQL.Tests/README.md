# Birko.Communication.GraphQL.Tests

Unit tests for the Birko.Communication.GraphQL library.

## Test Framework
- xUnit 2.9.3
- FluentAssertions 7.0.0
- Target: net10.0

## Test Classes
- GraphQLSettingsTests — Settings defaults and property mapping
- GraphQLExceptionTests — Exception constructors
- GraphQLRequestTests — Request construction and serialization
- GraphQLResponseTests — Response deserialization
- GraphQLRequestBuilderTests — Fluent builder API
- GraphQLClientTests — Client operations with FakeHttpHandler
- GraphQLSubscriptionTests — Subscription lifecycle

## Running Tests

```
dotnet test C:\Source\Birko.Communication.GraphQL.Tests\
```

## License

See [License.md](License.md)
