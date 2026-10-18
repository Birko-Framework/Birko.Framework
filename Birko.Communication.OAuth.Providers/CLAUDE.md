# Birko.Communication.OAuth.Providers

## Overview
Pre-configured OAuth client factories for specific services.

## Project Location
`Birko.Communication.OAuth.Providers/`

## Namespace
`Birko.Communication.OAuth.Providers`

## Components

### GitHubOAuthProvider.cs
- `GitHubOAuthProvider` — Factory methods for GitHub OAuth integration
  - `CreateDeviceFlowClient()` — Creates a pre-configured OAuth client for GitHub device flow authentication
  - `CreateDeviceFlowSettings()` — Creates default settings for GitHub device flow
  - `CreateWebFlowSettings(clientId, clientSecret, redirectUri, scope = "read:user user:email")` — web
    (authorization-code) sign-in settings; `AuthorizationEndpoint` constant (TASK-521)

### GitHubUserClient.cs (TASK-521)
- `IGitHubUserClient` / `GitHubUserClient(HttpClient, userAgent, apiBase?)` — `GetUserAsync` (`GET /user` → `GitHubUser(Id,
  Login, Name, Email)`; store `Id`, a login can be renamed) and `GetPrimaryEmailAsync` (`GET /user/emails`, the verified
  primary or null; `/user`'s email is null for a private address). Headers per request (User-Agent, which GitHub requires,
  `application/vnd.github+json`, API version), so a shared HttpClient is never modified. A refusal is an `OAuthException`
  with GitHub's status and message; a response without a numeric id is refused. `apiBase` for GitHub Enterprise Server.

## Dependencies
- **Birko.Communication.OAuth** — OAuth client infrastructure

## Consumers
- **Birko.AI.Providers** — `GitHubCopilotProvider` uses GitHub device flow authentication
- DraCode — GitHub web sign-in (adopts these in DraCode TASK-111)
