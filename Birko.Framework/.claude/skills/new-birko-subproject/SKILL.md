---
name: new-birko-subproject
description: Scaffold a new sibling `Birko.X` shared project inside the Birko.Framework workspace (e.g. a new store, model domain, serializer, communication adapter, AI provider, etc.). Use when the user says "novy birko projekt", "novy sub projekt", "add a new Birko project", "create Birko.Foo", "new persistence backend", "new model domain", "new Birko sibling", or similar requests to extend the framework itself (not to consume it — that's the [[birko-new-project]] skill). Creates `.shproj` + `.projitems` with proper hex GUIDs, the required `CLAUDE.md` / `README.md` / `License.md` / `.gitignore`, an optional `.Tests` companion project, and registers everything in `Birko.Framework.slnx`, `Birko.Framework.code-workspace`, and the `Birko.Framework.csproj` aggregator. Companion: [[new-store-backend]] (specializes this for persistence backends), [[verify-birko-conventions]] (post-scaffold lint).
---

# Birko Framework — New Sibling Project Scaffolder

Add a new `Birko.X` shared project to the Birko.Framework workspace, following the **New Project Checklist** documented in `C:\Source\Birko.Framework\CLAUDE-maintenance.md`.

This skill is the inverse of [[birko-new-project]]: that one scaffolds a **consumer** of Birko; this one extends Birko itself.

## Authoritative references — READ THESE FIRST when invoked

Before generating anything, open and re-read these files in `C:\Source\Birko.Framework\`. They are the source of truth — if anything in this skill drifts from them, **follow the files**.

- `CLAUDE-maintenance.md` — full new-project checklist, GUID requirements, solution + workspace folder groups, test + health-check requirements.
- `CLAUDE.md` — root architecture overview + dependency flow diagram (helps decide which existing project the new one depends on).
- `CLAUDE-projects.md` — catalog of existing projects; check whether the proposed work belongs in an existing project before creating a new one.
- `Birko.Framework.slnx` — canonical solution shape; copy a similar entry.
- `Birko.Framework.code-workspace` — workspace folder entries, alphabetically sorted within each Group.
- `Birko.Framework.csproj` — aggregator that imports every shared project's `.projitems`.
- **Reference siblings to copy from** — pick the closest existing project (e.g. `Birko.Data.ElasticSearch` for a new store, `Birko.Serialization.Yaml` for a new serializer, `Birko.AI.Providers` for a new AI provider) and mirror its structure.

## Inputs to gather from the user

Use `AskUserQuestion` to collect, in this order:

1. **Project name** — `Birko.X` (one segment) or `Birko.X.Y` (sub-area). The skill should reject names already on disk; check `C:\Source\` for an existing folder.
2. **Folder group** in `.code-workspace` and `.slnx` — pick from the existing groups listed in `CLAUDE-maintenance.md` (BackgroundJobs, Caching, Communication, Data, Health, Data.Migrations, Data.NoSQL, Data.Patterns, Data.SQL, Data.Sync, Data.ViewModels, Helpers, Models, Redis, Security, Serialization, Storage, Telemetry, Tests, CQRS, Rules, Validation, Time, Workflow, AI, Data.Views, EventBus, Localization, Messaging, Web). Offer to create a new group if none fits.
3. **Project shape** — most Birko projects are **Shared Projects** (`.shproj` + `.projitems`). A handful (e.g. test projects, `Birko.Framework.csproj` itself) are regular `.csproj`. Default = Shared Project. Use `.csproj` only when the user explicitly asks for a packable NuGet artifact.
4. **Dependencies** — which existing `Birko.X` projects this new one depends on. The aggregator imports `.projitems` transitively, but the new project's own `CLAUDE.md` and `README.md` should declare its direct deps.
5. **Companion test project?** — Default **yes**. Creates `Birko.X.Tests` as `.csproj` (xUnit + FluentAssertions, per the Test Requirements section).
6. **Companion health check?** — Only ask if the new project connects to an external service (database, queue, cache, API). If yes, follow the Health Check Requirements in `CLAUDE-maintenance.md`: pick `Birko.Health.Data` / `.Redis` / `.Azure`, or propose a new `Birko.Health.X`.

## Per-file checklist for the new project

Everything below comes from `CLAUDE-maintenance.md` § "New Project Checklist". Re-read that section if any detail here disagrees with it.

### 1. `Birko.X.shproj`

- Visual Studio Shared Project file.
- `<ProjectGuid>` must be a fresh **hex-only** GUID (`0-9`, `a-f`). Format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`. Do **not** put human-readable letters (`g-z`) in GUIDs.
- Generate with PowerShell: `[guid]::NewGuid().ToString()`.
- Copy structure from a similar existing `.shproj` (e.g. `C:\Source\Birko.Data.JSON\Birko.Data.JSON.shproj`).

### 2. `Birko.X.projitems`

- `<SharedGUID>` — must **match** the `.shproj` `<ProjectGuid>`.
- `<Import Project>` chain — copy from a similar project; usually `<Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\CodeSharing\Microsoft.CodeSharing.Common.Default.props" />` plus the matching `.targets`.
- `<ItemGroup>` of `<Compile Include="$(MSBuildThisFileDirectory)…" />` entries — list every `.cs` file. New projects typically start with one placeholder file you can replace.

### 3. `CLAUDE.md`

- Project-specific equivalent of root `CLAUDE.md`.
- Required sections: **Overview**, **Project Location** (`C:\Source\Birko.X\`), **Components** (key types + their responsibilities), **Dependencies** (which `Birko.*` it depends on, which NuGet packages), **Maintenance** (link back to root `CLAUDE-maintenance.md`).
- Copy structure from a comparable existing project's `CLAUDE.md` (e.g. `C:\Source\Birko.Data.ElasticSearch\CLAUDE.md`).

### 4. `README.md`

- Public-facing. Sections: project name + one-line purpose, **Features**, **Usage** (code snippet), **Test framework** (only for test projects), **Running tests**, **License**.

### 5. `License.md`

- MIT, copyright **2026 František Bereň**.
- Copy verbatim from any existing project's `License.md`. Do **not** regenerate from a template — copy the exact text.

### 6. `.gitignore`

- Standard Visual Studio `.gitignore`. Copy verbatim from any existing project.

### 7. (Optional) `azure-pipelines.yml`

- Some projects have one (e.g. `Birko.Data.ElasticSearch/azure-pipelines.yml`); most don't. Only add if the user asks.

## Registrations to update

After creating the project files, update **all** of these in the same change:

### `Birko.Framework.slnx`

Add a `<Project Path="…\Birko.X\Birko.X.shproj" />` entry inside the appropriate `<Folder>`. Look at neighbours in the same folder for the exact path style.

### `Birko.Framework.code-workspace`

Add a folder entry inside the matching Group, alphabetically sorted:

```json
{ "name": "Group / Birko.X", "path": "../Birko.X" }
```

### `Birko.Framework.csproj`

Add the `.projitems` import alongside the existing imports for that folder group:

```xml
<Import Project="..\Birko.X\Birko.X.projitems" Label="Shared" />
```

### Root `CLAUDE.md` — Dependency Flow

If the new project changes the dependency tree (most do), update the **Dependency Flow** ASCII diagram in `C:\Source\Birko.Framework\CLAUDE.md` so future reads of the architecture stay accurate.

### Root `README.md` + `docs/`

Per the user's [[feedback_update_docs]] preference, **always update `README.md` and `docs/`** when adding a new project. At minimum:

- `README.md` — add the new project to whatever catalog or feature list mentions its peers.
- `docs/dependencies.md` — add transitive NuGet deps if any.
- `docs/{area}.md` — if there's an area-specific doc (e.g. `docs/serialization.md`, `docs/stores.md`), add a section.

## Companion test project (if selected)

Create `C:\Source\Birko.X.Tests\` as a regular `.csproj` (NOT shared):

- `Microsoft.NET.Sdk` target framework matching the rest of the framework (`net10.0` currently).
- `<PackageReference>` for **xUnit**, **xUnit.runner.visualstudio**, **Microsoft.NET.Test.Sdk**, **FluentAssertions**.
- `<Import Project="..\Birko.X\Birko.X.projitems" Label="Shared" />` so tests have direct access to the project's source.
- Same `CLAUDE.md` / `README.md` / `License.md` / `.gitignore` requirements.
- Register in `.slnx` under `Tests/`, in `.code-workspace` under `Tests / Birko.X.Tests`.

## After scaffolding

1. **Verify the workspace loads** — open `Birko.Framework.code-workspace` in VS Code and confirm the new folder appears.
2. **Verify the solution builds** — `dotnet build C:\Source\Birko.Framework\Birko.Framework.slnx` (the aggregator `Birko.Framework.csproj` must compile with the new `.projitems` imported).
3. **Run the new project's tests** — `dotnet test C:\Source\Birko.X.Tests\Birko.X.Tests.csproj`.
4. **Consider running [[verify-birko-conventions]]** to catch nullable-warning / `*Core`-override / settings-passing issues before commit.
5. **Add a `Recent Updates` entry** to root `CLAUDE.md` describing what the new project does. Follow the format of the existing entries (header `### Birko.X (YYYY-MM-DD)`).

## What this skill does NOT do

- It does not write your domain code. After scaffolding, the project has one placeholder `.cs` file; you fill in the actual types.
- It does not add the project to *consumer* aggregators (like `Symbio.Birko.csproj`). That's a consumer-side change handled by [[birko-new-project]] or done manually in the consumer repo.
- It does not publish a NuGet package. Birko ships as source-only shared projects; consumers compile from source via `$(BirkoSrc)`.
