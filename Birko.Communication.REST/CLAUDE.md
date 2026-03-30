# Birko.Communication.REST

## Overview
REST API client implementation for Birko.Communication. Server-side functionality is in Birko.Communication.REST.Server.

## Project Location
`C:\Source\Birko.Communication.REST\`

## Purpose
- HTTP/HTTPS communication
- REST API client
- JSON serialization
- Authentication support

## Components

### Client
- `RestClient` - REST API client
- `AsyncRestClient` - Async REST API client

### Middleware
- Authentication middleware
- Logging middleware
- Retry middleware

### Models
- `RestRequest` - Request configuration
- `RestResponse` - Response data
- `RestSettings` - Client settings

## Basic Usage

```csharp
using Birko.Communication.REST;

var client = new RestClient("https://api.example.com");

// GET request
var response = await client.GetAsync("/users");
var users = response.GetData<List<User>>();

// POST request
var newUser = new User { Name = "John", Email = "john@example.com" };
var postResponse = await client.PostAsync("/users", newUser);
var created = postResponse.GetData<User>();

// PUT request
await client.PutAsync("/users/1", newUser);

// DELETE request
await client.DeleteAsync("/users/1");
```

## Authentication

### Bearer Token
```csharp
client.SetBearerToken("your-token-here");
```

### Basic Auth
```csharp
client.SetBasicAuth("username", "password");
```

### API Key
```csharp
client.AddDefaultHeader("X-API-Key", "your-api-key");
```

## Custom Headers

```csharp
client.AddDefaultHeader("User-Agent", "MyApp/1.0");
client.AddDefaultHeader("Accept", "application/json");
```

## Query Parameters

```csharp
var response = await client.GetAsync("/users", new
{
    page = 1,
    limit = 10,
    search = "john"
});
```

## Error Handling

```csharp
try
{
    var response = await client.GetAsync("/users/999");
    if (!response.IsSuccess)
    {
        Console.WriteLine($"Error: {response.StatusCode} - {response.Message}");
    }
}
catch (RestException ex)
{
    Console.WriteLine($"REST Exception: {ex.Message}");
}
```

## Dependencies
- Birko.Communication
- System.Net.Http
- System.Text.Json (or Newtonsoft.Json)

## Features

### Automatic Retries
```csharp
client.MaxRetries = 3;
client.RetryDelay = TimeSpan.FromSeconds(1);
```

### Timeout Configuration
```csharp
client.Timeout = TimeSpan.FromSeconds(30);
```

### Response Caching
```csharp
client.EnableCache(TimeSpan.FromMinutes(5));
```

## Use Cases
- Consuming REST APIs
- Third-party integrations
- Microservices communication
- Mobile app backends
- Webhook handlers

## Best Practices

1. **Timeouts** - Always set appropriate timeouts
2. **Retry logic** - Implement retry for transient failures
3. **Error handling** - Handle HTTP errors gracefully
4. **Cancellation tokens** - Use cancellation tokens for async operations
5. **Dispose** - Always dispose the client
6. **HTTPS** - Use HTTPS in production

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
