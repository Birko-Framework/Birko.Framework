# Birko.Data.TimescaleDB.Tests

## Overview
Unit tests for Birko.Data.TimescaleDB — stores, repositories, connector guards, and hypertable
configuration. Offline only; live-TimescaleDB paths are integration-tier.

## Project Location
`C:\Source\Birko\Framework.Tests\Birko.Data.TimescaleDB.Tests\`

## Scope
- `TimescaleDBStoreTests` — store construction + `TimescaleDBSettings` defaults
  (`TimeColumn` "timestamp", `ChunkTimeInterval` "7 days").
- `TimescaleDBConnectorConnectionStringTests` — CR-H109: `CreateConnection` honors
  `TimescaleDBSettings.GetConnectionString()` (timeouts, SSL).
- `TimescaleDBHypertableAndSettingsTests` — CR-M176/M177: `BuildCreateHypertableSql` escaping/INTERVAL
  formatting, RemoteSettings→TimescaleDBSettings chaining.
- `TimescaleDBGuardTests` — CR-L232 (both connector constructors throw `ArgumentNullException` on
  null settings instead of NRE-ing) + CR-L233 (unconfigured store/model-repository schema methods
  fail fast through the shared `RequireConnector()` with the "Call SetSettings" message). Also
  documents why the CR-L233 dead-repository-copy deletion has no reflection pin (the maintained
  same-named classes live in Birko.Data.TimescaleDB.ViewModel).

## Test Framework
xUnit + FluentAssertions (+ Moq referenced)
