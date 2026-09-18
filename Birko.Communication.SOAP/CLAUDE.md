# Birko.Communication.SOAP

## Overview
SOAP client implementation for Birko.Communication.

## Project Location
`C:\Source\Birko.Communication.SOAP\`

## Purpose
- SOAP web service client
- WSDL support
- XML serialization
- WS-Security

## Components

### Client
- `SoapClient` - SOAP client
- `AsyncSoapClient` - Async SOAP client

### Models
- `SoapRequest` - SOAP request
- `SoapResponse` - SOAP response
- `SoapSettings` - Client settings

## Basic Usage

```csharp
using Birko.Communication.SOAP;

var client = new SoapClient("https://api.example.com/service?wsdl");

var response = await client.CallAsync("GetUser", new
{
    userId = 123
});

var user = response.GetData<User>();
```

## WSDL Import

```csharp
var client = await SoapClient.FromWsdlAsync("https://api.example.com/service?wsdl");
```

## Authentication

### Basic Auth
```csharp
client.SetBasicAuth("username", "password");
```

### WS-Security
```csharp
client.SetWsSecurity(username, token);
```

## Custom Headers

```csharp
client.AddSoapHeader("Authentication", new
{
    Username = "user",
    Token = "abc123"
});
```

## Dependencies
- Birko.Communication
- System.ServiceModel.Primitives (or System.Web.Services)

## Use Cases
- Legacy web service integration
- Enterprise SOAP services
- Financial services
- Government services
- ERP integrations

## Best Practices

1. **Timeouts** - Set appropriate timeouts
2. **Error handling** - Handle SOAP faults
3. **Caching** - Cache WSDL when possible
4. **Security** - Use HTTPS and WS-Security
5. **Proxy** - Configure proxy when needed

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
