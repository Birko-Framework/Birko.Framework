# Birko.Communication.SOAP

SOAP web service client and server library for the Birko Framework, providing XML-based communication with authentication support.

## Features

- SOAP client for consuming SOAP/XML web services
- SOAP server using `HttpListener` with service registration
- XML request/response serialization
- Credential-based authentication
- Authentication middleware with configurable service
- Configurable request timeouts

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.SOAP\Birko.Communication.SOAP.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** - Base communication interfaces
- **System.Xml** / **System.Xml.Linq** - XML processing
- **Microsoft.Extensions.Logging** - Logging for the server

## Usage

### SOAP Client

```csharp
using Birko.Communication.SOAP;

var client = new SoapClient("https://api.example.com/service");
client.Credentials = new NetworkCredential("user", "pass");

// Call a SOAP action
var response = await client.CallAsync("GetUser", "<userId>123</userId>");
```

### SOAP Server

```csharp
using Birko.Communication.SOAP;

var server = new SoapServer("http://localhost:5000/");
server.OnRequest += (sender, context) =>
{
    // Handle incoming SOAP requests
};

await server.StartAsync();
```

### Authentication

```csharp
using Birko.Communication.SOAP.Middleware;

var config = new SoapAuthenticationConfiguration { /* ... */ };
var authService = new SoapAuthenticationService(config);
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `SoapClient` | Client for consuming SOAP web services (URI, Credentials, Timeout) |
| `SoapServer` | SOAP server using `HttpListener` with service registration, implements `IDisposable` |
| `SoapAuthenticationService` | Authentication logic for SOAP requests |
| `SoapAuthenticationConfiguration` | Authentication settings |

### Namespaces

- `Birko.Communication.SOAP` - Client and server classes
- `Birko.Communication.SOAP.Middleware` - Authentication services

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.REST](../Birko.Communication.REST/) - REST API client/server

## License

Part of the Birko Framework.
