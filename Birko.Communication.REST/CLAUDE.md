# Birko.Communication.REST

## Overview
REST API client implementation for Birko.Communication. Server-side functionality is in Birko.Communication.REST.Server.

## Purpose
- HTTP/HTTPS REST client over `HttpClient`
- Raw request/response as strings — the caller serializes/deserializes (there is no built-in JSON model layer)

## Components
One class: **`RestClient : IDisposable`** (namespace `Birko.Communication.REST`). There is **no**
`AsyncRestClient`, no `RestRequest`/`RestResponse`/`RestSettings` model types, no `RestException`, no
built-in retry/cache middleware — the surface is deliberately thin.
- `static RestClient GetClient(string baseUri)` — process-wide cached instance per base URI (thread-safe `ConcurrentDictionary`).
- `RestClient(string baseUri)` — direct construction.
- Properties: `BaseURI` (trimmed), `ICredentials? Credentials` (applied to the underlying `HttpClientHandler`; set before the first request), `int Timeout` (**milliseconds**, not `TimeSpan`), `string DefaultContentType` (default `application/json`), `Dictionary<string,string> DefaultHeaders`.
- Events: `OnRequest` / `OnResponse`.
- Verbs: sync `Get/Post/Put/Delete/Patch` → `string`; async `GetAsync/PostAsync/PutAsync/DeleteAsync/PatchAsync` → `Task<string>` (accept a `CancellationToken`). `HttpMethod` is the library's own enum.

## Basic Usage

```csharp
using Birko.Communication.REST;

using var client = new RestClient("https://api.example.com"); // or RestClient.GetClient(...)

// Returns the raw response body as a string — deserialize it yourself.
string usersJson = await client.GetAsync("/users");
var users = System.Text.Json.JsonSerializer.Deserialize<List<User>>(usersJson);

string created = await client.PostAsync("/users", body: newUserJson);
await client.PutAsync("/users/1", body: updatedJson);
await client.DeleteAsync("/users/1");
```

> The synchronous overloads (`Get`/`Post`/…) are convenience shims that block on the async methods;
> prefer the `*Async` overloads (only they take a `CancellationToken`).

## Authentication

Header-based auth via `DefaultHeaders` (there are no `SetBearerToken`/`SetBasicAuth` helpers):

```csharp
client.DefaultHeaders["Authorization"] = "Bearer your-token";
client.DefaultHeaders["X-API-Key"] = "your-api-key";
```

Or Windows/NTLM/basic credentials via the handler-backed property (set before the first request):

```csharp
client.Credentials = new System.Net.NetworkCredential("user", "pass");
```

## Query parameters & headers

Query string is passed as a raw string; per-request headers as a `Dictionary<string,string>`:

```csharp
string body = await client.GetAsync("/users", queryString: "page=1&limit=10", headers: null);
```

## Timeout

```csharp
client.Timeout = 30000; // milliseconds
```

## Dependencies
- Birko.Communication
- System.Net.Http

## Best Practices
1. Prefer the `*Async` overloads and pass a `CancellationToken`.
2. Set `Credentials` (or the `Authorization` default header) before the first request.
3. Dispose the client (or reuse the cached `GetClient` instance) — do not new-up per request.
4. Use HTTPS in production.

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
