# Birko Framework — Maintenance Guidelines

## README Updates
When making changes that affect the public API, features, or usage patterns of any project, update its README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

## CLAUDE.md Updates
When making major changes to a project, update its CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

## New Project Checklist
Every project directory must contain:

1. **`License.md`** — MIT license (Copyright 2026 František Bereň). Copy from any existing project.
2. **`README.md`** — Project name, overview, features, test framework (if test project), running instructions, and License section.
3. **`CLAUDE.md`** — Overview, project location, components, dependencies, and maintenance instructions.
4. **`.gitignore`** — Standard Visual Studio .gitignore. Copy from any existing project.

**GUID requirements for `.shproj` and `.projitems` files:**
- `ProjectGuid` in `.shproj` and `SharedGUID` in `.projitems` must be valid GUIDs containing **only hex characters** (`0-9`, `a-f`).
- Format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` (8-4-4-4-12 characters). Do NOT use human-readable names or non-hex letters (`g-z`) in GUIDs.
- Each project must have a unique GUID. Generate a proper random GUID (e.g., `b3a8c1d4-e5f6-4a7b-9c0d-1e2f3a4b5c6d`).

## Solution & Workspace Registration
When adding a new project, register in both:

1. **`Birko.Framework.slnx`** — Add `<Project>` in the appropriate `<Folder>`. Shared projects use `.shproj`, test projects use `.csproj`. Paths relative to `.slnx`.

2. **`Birko.Framework.code-workspace`** — Add folder entry with `"Group / Birko.ProjectName"` name convention. Keep entries sorted alphabetically.

Existing folder groups:
- **BackgroundJobs/** — Birko.BackgroundJobs.*
- **Caching/** — Birko.Caching, Birko.Caching.Redis, Birko.Caching.Hybrid
- **Communication/** — Birko.Communication.*
- **Data/** — Birko.Contracts, Birko.Data.Core, Birko.Configuration, Birko.Data.Stores, Birko.Data.Repositories
- **Health/** — Birko.Health, Birko.Health.Data, Birko.Health.Redis, Birko.Health.Azure
- **Data.Migrations/** — Birko.Data.Migrations.*
- **Data.NoSQL/** — ElasticSearch, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB stores
- **Data.Patterns/** — Birko.Data.Patterns, EventSourcing, Tenant
- **Data.SQL/** — Birko.Data.SQL, MSSql, MySQL, PostgreSQL, SqLite, View
- **Data.Sync/** — Birko.Data.Sync.*
- **Data.ViewModels/** — Birko.Data.*.ViewModel
- **Helpers/** — Birko.Helpers, Birko.Structures, Birko.Random
- **Models/** — Birko.Models.*
- **Redis/** — Birko.Redis
- **Security/** — Birko.Security, Birko.Security.Jwt/AspNetCore/BCrypt/Vault/AzureKeyVault/NFC
- **Serialization/** — Birko.Serialization, .Newtonsoft, .MessagePack, .Protobuf
- **Storage/** — Birko.Storage, Birko.Storage.AzureBlob
- **Telemetry/** — Birko.Telemetry, Birko.Telemetry.OpenTelemetry
- **Tests/** — All *.Tests projects
- **CQRS/** — Birko.CQRS
- **Rules/** — Birko.Rules
- **Validation/** — Birko.Validation
- **Time/** — Birko.Time.Abstractions, Birko.Time
- **Workflow/** — Birko.Workflow, Birko.Workflow.SQL/ElasticSearch/MongoDB/RavenDB/JSON

## Test Requirements
Every new public functionality must have corresponding unit tests:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions

## Health Check Requirements
When creating a project connecting to an external service, **automatically create a health check**:
- **Birko.Health.Data** — database/data store providers
- **Birko.Health.Redis** — Redis-specific checks
- **Birko.Health.Azure** — Azure cloud services
- **New Birko.Health.X project** — if doesn't fit existing

Health check pattern:
1. Implement `IHealthCheck` with lightweight connectivity probe (ping, SELECT 1, list maxResults=1)
2. Dual constructors: `Func<T>` factory and singleton instance
3. Three-level status: Healthy (OK), Degraded (slow > threshold), Unhealthy (exception)
4. Include `latencyMs` in result `Data` dictionary
5. Add unit tests for constructor validation, factory exception handling, cancellation
6. Update `docs/health.md`, health examples, and Health tab in Program.cs
7. Register in solution (.slnx), workspace (.code-workspace), and framework .csproj
