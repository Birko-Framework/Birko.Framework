# Birko.Communication.REST

REST API client and server library for the Birko Framework, providing HTTP-based communication with authentication middleware support.

## Features

- REST client for consuming RESTful APIs with configurable base URI
- REST server using `HttpListener` with route registration and middleware pipeline
- Authentication middleware with configurable authentication service
- Credential-based authentication support
- Configurable request timeouts
- Content type negotiation

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.REST\Birko.Communication.REST.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** - Base communication interfaces
- **System.Net.Http** - .NET HTTP APIs
- **Microsoft.Extensions.Logging** - Logging for the server

## Usage

### REST Client

```csharp
using Birko.Communication.REST;

var client = new RestClient("https://api.example.com");
client.Credentials = new NetworkCredential("user", "pass");

// GET request
var response = await client.GetAsync("/users");

// POST request
var json = JsonSerializer.Serialize(new { Name = "John" });
var postResponse = await client.PostAsync("/users", json, "application/json");

// PUT / DELETE
await client.PutAsync("/users/1", json, "application/json");
await client.DeleteAsync("/users/1");
```

### REST Server

```csharp
using Birko.Communication.REST;

var server = new RestServer("http://localhost:5000/");
server.OnRequest += (sender, context) =>
{
    // Handle incoming requests
};

await server.StartAsync();
```

### Authentication Middleware

```csharp
using Birko.Communication.REST.Middleware;

var config = new RestAuthenticationConfiguration { /* ... */ };
var authService = new RestAuthenticationService(config);
var middleware = RestAuthenticationMiddleware.Create(authService);
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `RestClient` | HTTP client for consuming REST APIs (BaseURI, Credentials, Timeout) |
| `RestServer` | HTTP server using `HttpListener` with route registration, implements `IDisposable` |
| `RestAuthenticationMiddleware` | Static factory for authentication middleware |
| `RestAuthenticationService` | Authentication logic for REST requests |
| `RestAuthenticationConfiguration` | Authentication settings |

### Namespaces

- `Birko.Communication.REST` - Client and server classes
- `Birko.Communication.REST.Middleware` - Authentication middleware

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.SOAP](../Birko.Communication.SOAP/) - SOAP web service client/server
- [Birko.Communication.SSE](../Birko.Communication.SSE/) - Server-Sent Events
- [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/) - WebSocket communication

## License

Part of the Birko Framework.
