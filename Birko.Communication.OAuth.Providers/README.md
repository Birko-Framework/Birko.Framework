# Birko.Communication.OAuth.Providers

Pre-configured OAuth client factories for specific services.

## Overview

Birko.Communication.OAuth.Providers offers ready-made factory methods that create properly configured OAuth clients for specific services, eliminating boilerplate setup.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `GitHubOAuthProvider` | `Birko.Communication.OAuth.Providers` | Factory for GitHub device flow OAuth clients |

## Dependencies

- **Birko.Communication.OAuth** — OAuth client infrastructure

## Usage

```xml
<Import Project="..\Birko.Communication.OAuth.Providers\Birko.Communication.OAuth.Providers.projitems" Label="Shared" />
```

```csharp
using Birko.Communication.OAuth.Providers;

var client = GitHubOAuthProvider.CreateDeviceFlowClient("client-id");
```

## License

MIT License - see [License.md](License.md)
