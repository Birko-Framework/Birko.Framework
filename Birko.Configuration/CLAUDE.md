# Birko.Configuration

## Overview
Lightweight shared project containing the settings hierarchy extracted from Birko.Data.Stores. Enables lightweight consumers to depend on connection settings without the full store abstraction layer.

## Project Location
`C:\Source\Birko.Configuration\`

## Namespace
`Birko.Configuration` — uses a distinct namespace to avoid collision with local `Settings` classes in other projects.

## Components

### Settings.cs
- **ISettings** — Interface extending `ILoadable<ISettings>` with `GetId()`
- **Settings** — Base class with `Location`, `Name` properties
- **PasswordSettings** — Extends Settings with `Password`
- **RemoteSettings** — Extends PasswordSettings with `UserName`, `Port`, `UseSecure`

All in namespace `Birko.Configuration`.

## Dependencies
- **Birko.Contracts** — `ILoadable<T>` interface (namespace `Birko.Data.Models`)

## Consumers
- **Birko.Data.Stores** — imports this project transitively (backward compatible)
- **Lightweight consumers** (MessageQueue, Communication, etc.) — import directly instead of Birko.Data.Stores

## Maintenance
- Keep namespace as `Birko.Configuration` — avoids collision with local `Settings` classes across the framework
- Any changes to Settings/PasswordSettings/RemoteSettings should be made here, not in Birko.Data.Stores
- Birko.Data.Stores no longer has its own Settings.cs — it imports this project
