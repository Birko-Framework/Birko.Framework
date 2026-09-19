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

## Dependencies
- **Birko.Communication.OAuth** — OAuth client infrastructure

## Consumers
- **Birko.AI.Providers** — `GitHubCopilotProvider` uses GitHub device flow authentication
