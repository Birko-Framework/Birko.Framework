# Birko.Health.Data.SQL.Tests

Tests for `Birko.Health.Data.SQL` — the schema-drift detection in `AbstractConnector.DetectDrift` and the
`SchemaDriftHealthCheck` that reports it.

## Scope

- **`SchemaDriftEndToEndTests`** — the mechanism end to end on **on-disk SQLite**, which needs no server.

## Why SQLite here

Measured at TASK-269 Step 0: `Symbio.Api/appsettings.json` reads `"Default": "SQLite"` in every
environment, and every non-test `DataProvider.MsSql` / `.MySql` / `.PostgreSql` reference across all 16
consumer repos is a switch case in a factory rather than a selection. So SQLite is the only provider with
a live population, and therefore the only one where drift can already exist in the field.

The three server providers carry their own `SchemaDriftLiveTests` in
`Birko.Data.SQL.{PostgreSQL,MySQL,MSSql}.Tests`, gated on `BIRKO_*_HOST` — their *rendering* is what needs
a live server (SQL Server's `max_length` is in bytes and must be halved for an `N`-type; PostgreSQL's
`format_type` speaks its own vocabulary and is canonicalised).

## Conventions

- **Never spell out an expected SQL type by hand for the healthy case.** The declared side comes from
  `ConvertType`, the method `CREATE TABLE` uses; a test that restated the mapping would be a second
  implementation of the rule and would keep passing while the DDL and the check drifted apart.
- **A table created by raw DDL is how an "old database" is simulated** — that is the only way to produce a
  column the framework would never emit today.

## Running

```
dotnet test --nologo
```

xUnit + FluentAssertions, per the framework convention.
