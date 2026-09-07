# Birko.Health.Data.SQL

A health check that reports **SQL schema drift** — columns whose stored type no longer matches what the
model declares — together with any index the connector failed to build.

## Why it exists

Birko does not repair an existing database. `CREATE TABLE` is guarded by `IF NOT EXISTS` and
schema-ensure never reconciles an existing table's columns, so changing a model — adding
`[MaxLengthField]`, changing a decimal's precision, changing a property's type — leaves the old column
in place. Without this check the first sign is an exception on whichever request happens to touch that
column first.

## Features

- **Column drift** — compares each entity's declared columns against the table behind it, per provider,
  including the width and scale (`DECIMAL(18,0)` vs `DECIMAL(18,2)` is drift, and is invisible to the
  provider-independent reader APIs).
- **Index failures** — surfaces `AbstractConnector.IndexCreationFailures`, which schema-ensure records
  rather than throwing, and which nothing else reads.
- **Degraded, never Unhealthy** — the database disagreeing with the models is a condition for a human to
  act on, not a reason to pull an instance out of a load balancer.
- **Honest about what it could not answer** — a provider with no readable column catalogue, or a table
  that does not exist yet, is reported as such rather than as a clean bill of health.

## Usage

```csharp
var check = new SchemaDriftHealthCheck(
    () => DataBase.GetConnector<SqLiteConnector>(settings),
    new[] { typeof(Customer), typeof(Invoice) });

var result = await check.CheckAsync(ct);
// result.Status == HealthStatus.Degraded
// result.Data["drift"] -> ["Invoice.Total: declared NUMERIC(18,2), stored REAL"]
```

Registered like any other `IHealthCheck`, e.g. through `HealthCheckRegistration`.

## Dependencies

- `Birko.Health` — the `IHealthCheck` contract and `HealthCheckResult`.
- `Birko.Data.SQL` — `AbstractConnector.DetectDrift` and `SchemaDriftReport`.

This is a separate project rather than a file in `Birko.Health.Data` so that leaf stays dependency-free;
see the comment in `Birko.Health.Data.SQL.projitems` for the measurement behind that.

## License

MIT — see [License.md](License.md).
