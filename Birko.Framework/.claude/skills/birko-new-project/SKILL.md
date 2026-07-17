---
name: birko-new-project
description: The Birko front door and .NET/Birko code-wiring step for consuming Birko.Framework via the aggregator pattern — wires Directory.Build.props with the $(BirkoSrc) / BIRKO_SRC convention, creates the aggregator project(s) with the correct .projitems imports, and registers everything in the solution file. Supports Web API / Console / worker / Class library / Web UI (Birko.Web.*) shapes. Use when the user says "novy projekt s birko", "new Birko project", "scaffold birko app", "pridaj birko do existujucej solution", "add Birko to this solution", "setup birko consumer", "wire Birko into my project", or wants another Birko aggregator/consumer project in an existing solution. IMPORTANT — for a BRAND-NEW project this skill FIRST hands off to the generic [[new-project]] skill (it builds the universal layer: README, CLAUDE.md, docs/, docs/features/, docs/specs/, tasks/) and does the Birko wiring as its step-5 stack scaffolder; never skip that handoff or the repo ends up with no CLAUDE.md / docs layer. For an EXISTING repo that already has its docs/tasks layer, run the wiring directly.
---

# Birko Framework — Consumer Project Scaffolder

Bootstrap a new .NET solution (or add into an existing one) that consumes Birko.Framework via the **aggregator pattern** documented in `C:\Source\Birko\Framework\Birko.Framework\README.md` ("Usage in Consumer Solutions").

> **Scope & layering:** this skill does the **.NET/Birko code wiring** (`Directory.Build.props`, aggregator csproj, `.slnx`, projitems imports). The tech-agnostic universal layer (README, CLAUDE.md seeded with the feature lifecycle, `.gitignore`, `docs/features/`, `docs/specs/`, `tasks/`) belongs to the generic [[new-project]] skill — this skill *builds on top of it*, never the other way around (the generic skill knows only a "stack scaffolder" extension hook, not Birko).
>
> **Brand-new project?** Invoke [[new-project]] FIRST and answer its "stack scaffolder" intake question with this skill — it runs the universal layer and calls back into the wiring below at its step 5. Only when the target repo already has its docs/tasks layer do you run the wiring below standalone.

## What this skill does

Given a target location, project name and selected Birko components, produce:

1. **`Directory.Build.props`** at the solution root — wiring `$(BirkoSrc)` with the canonical 3-step resolution (`/p:BirkoSrc=…` → `BIRKO_SRC` env → parent dir default).
2. **Aggregator csproj(s)** — `{Solution}.Birko.csproj` (or split by layer: `.Birko.Core`, `.Birko.Edge`, `.Birko.Ai`, `.Birko.Web`) with the necessary `<Import Project="$(BirkoSrc)\Birko.X\Birko.X.projitems" Label="Shared" />` lines and the matching `<PackageReference>` for transitive NuGet deps.
3. **Target app project** — Web API / worker / class library / Web UI host — referencing the aggregator(s) via `<ProjectReference>`.
4. **Solution file** — either a new `.slnx` (preferred — matches Birko.Framework itself) or appends entries to an existing one.
5. **For Web UI shapes only** — minimal `build.js` esbuild config + `package.json` that reads `BIRKO_SRC` with the same fallback (so backend MSBuild and frontend esbuild share one env override; critical for Docker/CI).
6. **License.md, README.md, .gitignore** — copied/adapted from `C:\Source\Birko\Framework\Birko.Framework`.

## Inputs to gather from the user

Use `AskUserQuestion` to collect, in this order:

1. **Solution / target location** — absolute path. If it doesn't exist → new solution; if it exists and contains a `.slnx`/`.sln` → add to existing solution.
2. **Solution name** (if new) — used as the prefix for aggregator + app project (e.g. `Acme` → `Acme.Birko`, `Acme.Api`).
3. **App project shape** — one or more of:
   - **Web API** (`Microsoft.NET.Sdk.Web`, `net10.0`, `Program.cs` minimal API host)
   - **Console / Worker** (`Microsoft.NET.Sdk.Worker` or plain `Microsoft.NET.Sdk` with `Program.cs`)
   - **Class library** (`Microsoft.NET.Sdk`, no entry point)
   - **Web UI** (Web API host + frontend `src/` with esbuild — uses `Birko.Web.Core`/`.Components`/`.Shell`)
4. **Birko component groups** the project needs. Show as a multi-select grouped by area (see "Component catalog" below). Always implicitly include `Birko.Contracts` + `Birko.Data.Core` + `Birko.Configuration`.
5. **Aggregator shape** — single (`{Solution}.Birko`) or split (`.Birko.Core` / `.Birko.Edge` / `.Birko.Ai` / `.Birko.Web`). Default = **single**. Recommend splitting only when:
   - Edge/IoT (`Birko.Communication.Hardware/.Bluetooth/.Camera/.Modbus`) is selected alongside Web/API → split into `.Core` + `.Edge`
   - Heavy AI (`Birko.AI.Providers`, `Birko.AI.Agents`) is selected alongside lean services → split off `.Birko.Ai`
   - Web UI is selected → split `.Birko.Web` (it pulls in `Birko.Web.Core/.Components/.Shell` source-only TS, doesn't need to live with backend csproj)

## Authoritative references — READ THESE FIRST when invoked

Before writing anything, open and re-read these files in `C:\Source\Birko\Framework\Birko.Framework\` (they may have evolved since this skill was written):

- `README.md` — section "Usage in Consumer Solutions" (around line 367) — canonical aggregator pattern, `Directory.Build.props` template, Docker example.
- `CLAUDE.md` — section "Usage in Consumer Solutions" — short form of the same rule.
- `CLAUDE-projects.md` — full catalog of `Birko.*` projects with one-line purpose each.
- `docs/consumers.md` — live examples (Symbio = single aggregator with ~90 projects; WebFinstatApiTester = no aggregator).
- `docs/dependencies.md` — which projects pull which NuGet packages (so you know which `<PackageReference>` lines to add to the aggregator).
- `Birko.Framework.slnx` — canonical `.slnx` shape (folder grouping, `<Project>` entries).

If anything in this skill contradicts those files, **follow the files** — they are the source of truth, this skill is a checklist.

## Directory.Build.props — canonical template

```xml
<Project>
  <PropertyGroup>
    <!-- Resolution order:
           1. /p:BirkoSrc=...    MSBuild CLI parameter (highest priority)
           2. BIRKO_SRC env var  Shell environment
           3. Default            Parent directory of this Directory.Build.props
         The default assumes Birko.* folders are sibling checkouts of your repo
         (i.e. C:\Source\Birko\Framework\Birko.Helpers next to C:\Source\YourSolution\). -->
    <BirkoSrc Condition="'$(BirkoSrc)' == '' and '$(BIRKO_SRC)' != ''">$(BIRKO_SRC)</BirkoSrc>
    <BirkoSrc Condition="'$(BirkoSrc)' == ''">$(MSBuildThisFileDirectory)..</BirkoSrc>
  </PropertyGroup>
</Project>
```

Write this verbatim to `{SolutionRoot}/Directory.Build.props`. If the file already exists, **merge** — don't overwrite.

## Aggregator csproj — canonical template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Birko</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <!-- FrameworkReference only when aggregator hosts ASP.NET types -->
    <!-- <FrameworkReference Include="Microsoft.AspNetCore.App" /> -->

    <!-- NuGet packages required by the imported Birko.* projitems.
         Look these up in docs/dependencies.md or by reading each Birko.X.projitems
         and grepping for <PackageReference> inside Birko.X. -->
    <!-- <PackageReference Include="Npgsql" Version="9.*" /> -->
  </ItemGroup>

  <!-- Birko.* shared projects — one Import per .projitems used by this aggregator.
       Do NOT also import a projitems that another selected one pulls in
       transitively (Data.Core imports Contracts; Data.Stores imports
       Configuration; Time imports Time.Abstractions) — a duplicate import emits
       MSB4011 and the second import is ignored. See "Edge cases / gotchas". -->
  <Import Project="$(BirkoSrc)\Birko.Data.Core\Birko.Data.Core.projitems"             Label="Shared" />
  <Import Project="$(BirkoSrc)\Birko.Data.Stores\Birko.Data.Stores.projitems"         Label="Shared" />
  <!-- … one Import per selected Birko.* projitems. Contracts + Configuration
       arrive transitively via the two above — no direct Import needed. -->
</Project>
```

Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` only if any selected Birko project hosts ASP.NET types (e.g. `Birko.Security.AspNetCore`, `Birko.Communication.REST.Server`, `Birko.Communication.SSE`).

### Discovering NuGet `<PackageReference>` lines

For each selected `Birko.X` projitems, open `C:\Source\Birko\Framework\Birko.X\Birko.X.projitems` and look for `<PackageReference>` blocks (rare — Birko projects mostly delegate package refs to the consumer aggregator). Then check `C:\Source\Birko\Framework\Birko.Framework\docs\dependencies.md` for the canonical list. Common ones:

| Birko project | Required PackageReference |
|---|---|
| `Birko.Data.SQL.PostgreSQL` | `Npgsql` 9.* |
| `Birko.Data.SQL.MSSql` | `Microsoft.Data.SqlClient` |
| `Birko.Data.SQL.MySQL` | `MySqlConnector` |
| `Birko.Data.SQL.SqLite` | `Microsoft.Data.Sqlite` |
| `Birko.Data.MongoDB` | `MongoDB.Driver` 3.* |
| `Birko.Data.ElasticSearch` | `NEST` / `Elastic.Clients.Elasticsearch` |
| `Birko.Data.RavenDB` | `RavenDB.Client` |
| `Birko.Data.CosmosDB` | `Microsoft.Azure.Cosmos` |
| `Birko.Data.InfluxDB` | `InfluxDB.Client` |
| `Birko.Data.TimescaleDB` | `Npgsql` 9.* |
| `Birko.Serialization.Yaml` | `YamlDotNet` |
| `Birko.Serialization.MessagePack` | `MessagePack` |
| `Birko.Serialization.Protobuf` | `protobuf-net` |
| `Birko.Communication.Camera` | `FFMpegCore` |
| `Birko.Caching.Redis` / `Birko.BackgroundJobs.Redis` | `StackExchange.Redis` |
| `Birko.AI.Providers` (Claude/OpenAI/Gemini/Ollama) | none — pure HTTP |

Always re-verify by reading the actual projitems and `docs/dependencies.md`; the table above can rot.

## Target app project templates

### Web API (`{Solution}.Api.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\{Solution}.Birko\{Solution}.Birko.csproj" />
  </ItemGroup>
</Project>
```

Minimal `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register Birko services here, e.g.:
// builder.Services.AddBirkoStores(opts => ...);

var app = builder.Build();
app.MapGet("/", () => "Hello from {Solution}.Api");
app.Run();
```

### Worker / Console (`{Solution}.Worker.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\{Solution}.Birko\{Solution}.Birko.csproj" />
  </ItemGroup>
</Project>
```

### Class library (`{Solution}.Lib.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\{Solution}.Birko\{Solution}.Birko.csproj" />
  </ItemGroup>
</Project>
```

### Web UI (frontend on top of Web API)

Backend csproj = same as **Web API** above, plus the Web UI aggregator (`{Solution}.Birko.Web`) should NOT import any TS sources via msbuild — TS is consumed by esbuild directly.

Frontend layout next to the api csproj:

```
{Solution}.Web/
  {Solution}.Web.csproj       (Microsoft.NET.Sdk.Web)
  Program.cs
  src/
    index.ts
    main.css
  package.json
  build.js
  wwwroot/                     (esbuild output)
```

`package.json`:

```json
{
  "name": "{solution-kebab}-web",
  "private": true,
  "scripts": {
    "build": "node build.js",
    "watch": "node build.js --watch"
  },
  "devDependencies": {
    "esbuild": "^0.24.0",
    "typescript": "^5.6.0"
  }
}
```

`build.js`:

```js
import esbuild from 'esbuild';

const BIRKO_SRC = (process.env.BIRKO_SRC ?? 'C:/Source').replace(/\/+$/, '');

const aliases = {
  'birko-web-core':       `${BIRKO_SRC}/Birko.Web.Core/src/index.ts`,
  'birko-web-components': `${BIRKO_SRC}/Birko.Web.Components/src/index.ts`,
  'birko-web-shell':      `${BIRKO_SRC}/Birko.Web.Shell/src/index.ts`,
};

const aliasPlugin = {
  name: 'birko-alias',
  setup(build) {
    for (const [from, to] of Object.entries(aliases)) {
      const filter = new RegExp(`^${from}(/.*)?$`);
      build.onResolve({ filter }, args => {
        const suffix = args.path.slice(from.length);
        return { path: to.replace('/index.ts', suffix ? suffix : '/index.ts') };
      });
    }
  },
};

const ctx = await esbuild.context({
  entryPoints: ['src/index.ts'],
  bundle: true,
  outdir: 'wwwroot',
  format: 'esm',
  target: 'es2022',
  sourcemap: true,
  plugins: [aliasPlugin],
});

if (process.argv.includes('--watch')) {
  await ctx.watch();
  console.log('watching…');
} else {
  await ctx.rebuild();
  await ctx.dispose();
}
```

`src/index.ts`:

```ts
import { defineComponents } from 'birko-web-components';
import { mountShell } from 'birko-web-shell';

defineComponents();
mountShell(document.body, { /* modules */ });
```

### Headless E2E / browser automation (Web UI shape)

**Scaffold the harness here; let [[populate-tests]] author the specs.** This skill OWNS the Birko.Web
`tests/ui-e2e/` wiring template below (the project-layer specifics that the stack-agnostic
[[populate-tests]] `adopt` mode defers to). For a Web UI consumer, drop these files in (after the Web UI
files exist — see *After writing*), then run `/populate-tests populate` to author the manifest smoke +
per-module CRUD on top and `/populate-tests verify` to run + triage.

Gotchas this template encodes (each cost a real debugging cycle): a single `@playwright/test` instance
(the package injects it via `withBirkoFixtures`, never imports its own), **no `"type":"module"`** in the
test package (pure-ESM breaks named imports from the CJS runner), and an even-numbered Node LTS (20/22/24
— never odd 21/23). `<REL>` = the consumer's relative path up to `Web/Birko.Web.Testing` — mirror the
depth used for its `birko-web-*` aliases (Symbio: `../../../../Web/Birko.Web.Testing`).

`tests/ui-e2e/package.json` (**no `"type":"module"`**):
```jsonc
{ "name": "<consumer>-ui-e2e", "private": true,
  "scripts": { "test": "playwright test", "install:browser": "playwright install chromium" },
  "devDependencies": { "@playwright/test": "^1.49", "@types/node": "^22", "puppeteer": "^23",
    "birko-web-testing": "file:<REL>" } }
```
`tests/ui-e2e/tsconfig.json` `paths`: `birko-web-testing`→`<REL>/src/index.ts`,
`birko-web-testing/*`→`<REL>/src/*/index.ts`, plus `@playwright/test`/`puppeteer`→`./node_modules/...`
(the path-mapped package resolves its driver deps relative to its own dir). `moduleResolution: bundler`.
`tests/ui-e2e/playwright.config.ts`:
```ts
import { defineConfig, devices } from '@playwright/test';
import { birkoPlaywrightPreset } from 'birko-web-testing/playwright';
export default defineConfig(birkoPlaywrightPreset({
  baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
  storageState: '.auth/state.json', desktopChrome: devices['Desktop Chrome'] }));
```
`tests/ui-e2e/fixtures.ts` (single-instance injection — specs import `test`/`expect` from HERE):
```ts
import { test as base, expect } from '@playwright/test';
import { withBirkoFixtures } from 'birko-web-testing/playwright';
export const test = withBirkoFixtures(base);
export { expect };
```
`tests/ui-e2e/auth.setup.ts` — login once via `loginViaApi(...)` → seed `localStorage[storageKey]` →
`storageState` (pass `claimMappings` if the app overrides them; `requireAuth:false` in the preset skips
it for public apps). `.gitignore`: `node_modules/ .auth/ test-results/ playwright-report/`. One-time:
even-LTS Node, then in `tests/ui-e2e/` run `npm install` + `npx playwright install chromium`.

**Test data isolation** — this lane drives a live API/DB (CRUD page objects really create/update/delete;
even the smoke logs in + hits each route on mount). Target a **disposable test env** — throwaway DB or
tenant, dedicated test account — never dev/prod. Keep the committed `baseURL`/creds on `localhost` and
guard CI off any prod host. See the package README's *Test data isolation* section.

Background on what gets wired — the shared **`birko-web-testing`** package
(`C:\Source\Birko\Web\Birko.Web.Testing`, pkg `birko-web-testing`) is a fourth source-only `Birko/Web/*`
sibling, resolved by a tsconfig `paths` entry (Node test runner, **not** esbuild — no `build.js` alias).
It splits two lanes over a shared selector/auth core:

- **Playwright = the test suite** (smoke route-sweep + CRUD), via `@playwright/test`. Spread
  `birkoPlaywrightPreset({ baseURL })` into `playwright.config.ts`; an `auth.setup.ts` logs in via
  `loginViaApi(...)` and seeds the `localStorage[storageKey]` snapshot; `runSmoke(manifest)` sweeps routes.
- **Puppeteer = utility scripts** (PDF/screenshot/perf/scrape), via `launchSession(...)` — NOT a second suite.

Selectors for the `b-*` components (data-table row actions, dropdown, form, sidebar/ribbon, the
`BaseCrudPage` `#btn-create`/`#modal`/`#form`/`#btn-save` ids) live in the package, grounded in the
real component source. **Browser policy:** ONE Playwright-managed Chromium per machine
(`npx playwright install chromium`); Puppeteer reuses it (`PUPPETEER_SKIP_DOWNLOAD=1` +
`PUPPETEER_EXECUTABLE_PATH`). Never install the drivers globally; pin them per consumer.

Reference implementation to copy: `C:\Source\Birko\Consumers\Symbio\tests\ui-e2e\`
(`playwright.config.ts` + `auth.setup.ts` + `smoke.spec.ts` + `communication.spec.ts`). The consumer's
tsconfig must also map the driver deps (`"@playwright/test"`, `"puppeteer"` → `./node_modules/...`)
because the path-mapped package resolves them relative to its own location. See the package
`README.md` / `ENV.md` for the full adoption + env-var reference. Earlier this was an ad-hoc
`verify.mjs` (Puppeteer smoke) — that still works for a quick render check, but `birko-web-testing`
is the canonical, reusable choice.

## Solution file — new vs existing

### New `.slnx`

Use the same shape as `C:\Source\Birko\Framework\Birko.Framework\Birko.Framework.slnx`:

```xml
<Solution>
  <Folder Name="/Birko/">
    <Project Path="{Solution}.Birko/{Solution}.Birko.csproj" />
  </Folder>
  <Folder Name="/App/">
    <Project Path="{Solution}.Api/{Solution}.Api.csproj" />
  </Folder>
</Solution>
```

Place at `{SolutionRoot}/{Solution}.slnx`.

### Existing solution

- If existing file is `.slnx` → parse + insert `<Project Path="…" />` entries in matching `<Folder>` (create folder if needed).
- If existing file is legacy `.sln` → run `dotnet sln {Solution}.sln add {csproj path}` for each project. Do not hand-edit `.sln` GUIDs.

Always confirm with the user before mutating an existing solution file.

## Pre-flight checks before writing files

1. **`C:\Source\Birko\Framework\Birko.Framework` is checked out** — required for `$(BirkoSrc)` default to resolve. Run `Test-Path C:\Source\Birko\Framework\Birko.Framework\Birko.Contracts\Birko.Contracts.projitems`. If missing, warn the user and offer to set `BIRKO_SRC` to a custom path before generating.
2. **Target dir is empty (for new)** or **contains a recognized solution (for add)** — abort if it's a non-empty dir with no solution and the user picked "new".
3. **Selected Birko projitems exist** — for every `Birko.X` the user selects, verify `$(BirkoSrc)\Birko.X\Birko.X.projitems` exists. If not, surface the typo before writing.
4. **No accidental ancestor git repo over the target** — run `git -C <target-dir> rev-parse --show-toplevel`. If it returns an **ancestor** of the target (e.g. `C:\Source` when the project is `C:\Source\MyApp`), warn the user: a parent folder that just holds sibling checkouts should not itself be a git repo, or the new project's git root resolves to the wrong directory. Offer to remove the stray ancestor `.git` (only if it has no commits/remote/tracked files — verify with `rev-list --all --count`, `remote -v`, `ls-files`) before continuing.
5. **Universal layer present (standalone runs only)** — this skill does **not** create the agent guide / docs layer (that's [[new-project]]'s job; see the Scope note at the top). When run **standalone** (not chained from [[new-project]]), check the solution root for an agent guide: `Test-Path {SolutionRoot}\CLAUDE.md` (or `AGENTS.md`). If **neither exists**, don't silently produce a Birko-wired repo with no agent guide — warn the user that the universal layer is missing and offer to generate it (render [[new-project]]'s `templates/CLAUDE.seed.md` for `CLAUDE.md`, plus `docs/` + `docs/features/README.md`), or to run [[new-project]] for the full layer. Skip this check when chained from [[new-project]] (that skill already owns the agent guide). This is the exact gap that left an early scaffold without a `CLAUDE.md`.

## After writing

1. Run `dotnet restore` from `{SolutionRoot}` and report failures (commonly missing `<PackageReference>` in the aggregator — fix and re-run).
2. Run `dotnet build {Solution}.slnx -c Debug` and report failures.
3. For Web UI: also run `npm install` then `npm run build` in `{Solution}.Web/`.
4. **For Web UI: scaffold the E2E harness** — write the `tests/ui-e2e/` files from the template in the
   *Headless E2E* section above (compute `<REL>`), run `npm install` + `npx playwright install chromium`
   there, then hand off to `/populate-tests populate` to author the manifest smoke + per-module CRUD.
   Skip if the consumer has no Birko.Web UI.
4. **Ask whether to git-track the solution, and confirm the root** (when run standalone — if chained from [[new-project]], that skill owns the git step, don't duplicate). Never silently `git init`. Run `git -C {SolutionRoot} rev-parse --show-toplevel` to learn the state, then ask via `AskUserQuestion`:
   - Returns `{SolutionRoot}` itself → already tracked here, nothing to do.
   - Fails (no repo up the tree) → ask **"Track this solution with git?"** (default Yes → `git init {SolutionRoot}`; No → skip).
   - Returns an **ancestor** (≠ `{SolutionRoot}`, e.g. `C:\Source`) → surface the resolved ancestor root and ask whether that's the intended repo root: give the solution its own repo (`git init {SolutionRoot}`), or treat the ancestor as an accidental repo over sibling checkouts and offer to remove its stray `.git` after verifying it's empty (`rev-list --all --count` = 0, no `remote -v`, no `ls-files`). Don't assume — only the user knows.
   Don't commit unless the user asks.
5. Print next-step checklist to the user:
   - Add real Birko service registrations in `Program.cs`
   - Configure connection strings / `appsettings.json` for selected stores
   - For Docker: copy the Dockerfile snippet from `README.md` and set `ENV BIRKO_SRC=/src`

## Component catalog (for the multi-select)

Group component groups as in `CLAUDE-projects.md`. Suggested groupings to show:

- **Core (always implicit)** — `Birko.Contracts`, `Birko.Configuration`, `Birko.Data.Core`, `Birko.Data.Stores`, `Birko.Data.Repositories`, `Birko.Helpers`. **Don't emit direct `<Import>` lines for `Birko.Contracts` or `Birko.Configuration`** — `Birko.Data.Core` imports Contracts and `Birko.Data.Stores` imports Configuration transitively, so a direct import duplicates them (MSB4011). Importing `Data.Core` + `Data.Stores` is enough to make both available.
- **SQL stores** — `Birko.Data.SQL` + one of `.MSSql` / `.PostgreSQL` / `.MySQL` / `.SqLite`
- **NoSQL stores** — `Birko.Data.MongoDB`, `.ElasticSearch`, `.RavenDB`, `.CosmosDB`, `.InfluxDB`, `.TimescaleDB`, `.JSON`, `.XML`
- **Models** — `Birko.Models`, `.Models.Contracts`, `.Models.Customers`, `.Models.Users`, `.Models.Product`, `.Models.Category`, `.Models.Inventory`, `.Models.Pricing`, `.Models.SEO`
- **Models SQL mappings (opt-in per persisted domain)** — `.Models.SQL` (fluent mapping framework, required if you import any `.Models.*.SQL`) + zero or more of `.Models.Users.SQL`, `.Models.Customers.SQL`, `.Models.Inventory.SQL`, `.Models.Pricing.SQL`, `.Models.Product.SQL`. Pick only siblings whose domain models you actually persist via SQL — NoSQL-only or read-only consumers should skip all of these. (The repo's `Birko.Framework.csproj` imports all five for build validation; consumer aggregators should not mirror that.)
- **Data features** — `Birko.Data.Patterns`, `.Tenant`, `.Composition`, `.Tagging`, `.Migrations` (+ provider), `.Sync` (+ provider), `.EventSourcing`, `.Views` (+ provider), `.Aggregates`
- **Communication** — `Birko.Communication.REST/.REST.Server/.SOAP/.WebSocket/.SSE/.Network/.Hardware/.Bluetooth/.Modbus/.Camera/.IR/.NFC/.OAuth/.GraphQL`
- **Messaging / Queues / Bus** — `Birko.Messaging`, `.Messaging.Razor`, `Birko.MessageQueue`, `Birko.EventBus`, `Birko.BackgroundJobs` (+ backend)
- **Workflow / Rules / CQRS / Validation** — `Birko.Workflow` (+ backend), `Birko.Rules`, `Birko.CQRS`, `Birko.Validation`
- **AI** — `Birko.AI.Contracts`, `Birko.AI`, `.Providers`, `.Agents`, `.Orchestration`, `.Resilience`
- **Security** — `Birko.Security`, `.Jwt`, `.AspNetCore`, `.BCrypt`, `.Vault`, `.AzureKeyVault`, `.NFC`
- **Cross-cutting** — `Birko.Caching` (+ backend), `Birko.Storage` (+ backend), `Birko.Telemetry`, `Birko.Health`, `Birko.Serialization` (+ format)
- **Time / Random / Structures** — `Birko.Time.Abstractions`, `Birko.Time`, `Birko.Random`, `Birko.Structures`
- **Web UI** — `Birko.Web.Core`, `.Components`, `.Shell` (TS, no msbuild import; see Web UI section above)

Map each selected entry to:
1. `<Import Project="$(BirkoSrc)\Birko.X\Birko.X.projitems" Label="Shared" />` in the appropriate aggregator
2. Any required `<PackageReference>` in the aggregator
3. For Web UI projects: TS alias entry in `build.js`, not an MSBuild import

## Edge cases / gotchas

- **`$(BirkoSrc)` resolves to the parent of `Directory.Build.props`** by default. If the user's repo lives at `C:\Source\YourSolution\` and Birko at `C:\Source\Birko\Framework\Birko.X\`, the default Just Works. If their layout is different, they must set `BIRKO_SRC` env var or pass `/p:BirkoSrc=…`.
- **GUIDs in `.shproj`/`.projitems`** — never edit Birko's own GUIDs from a consumer; only generate fresh hex GUIDs for any *new* shared project (out of scope here — this skill creates csproj consumers, not shared projects).
- **Don't import overlapping projitems from two different aggregators** consumed by the same downstream csproj — that's the exact failure mode the aggregator pattern exists to prevent. Each downstream project should reference **one** aggregator per dependency direction.
- **Don't import a projitems that another selected projitems already imports transitively** (within the *same* aggregator). Known chains: `Birko.Data.Core` → `Birko.Contracts`, `Birko.Data.Stores` → `Birko.Configuration`, `Birko.Time` → `Birko.Time.Abstractions`. A duplicate import emits `MSB4011: "…projitems" cannot be imported again … This subsequent import will be ignored.` — non-fatal but noisy. The repo's `Birko.Framework.csproj` documents these omissions inline (e.g. *"Birko.Time.Abstractions is transitively imported via Birko.Time.projitems"*). After generating the aggregator, run `dotnet build` once and drop the direct `<Import>` for anything that warns MSB4011.
- **Birko.Framework's own slnx is NOT a template for consumer slnx** — it groups by component family, consumers usually group by app layer (`Birko/`, `App/`, `Tests/`).
- **`net10.0`** is the current target for all Birko projects. Don't downgrade.

## Don't do

- Don't hard-code absolute paths to `C:\Source\Birko\Framework\Birko.X\…` in `<Import>` — always use `$(BirkoSrc)`.
- Don't add the same projitems Import in both the aggregator and the app csproj — pick one (aggregator).
- Don't pin `<TargetFramework>` to something older than `net10.0` without asking.
- Don't generate `Co-Authored-By` lines in any commit messages you create (user preference — see memory `feedback_no_coauthor.md`).
- Don't skip updating the solution's own `README.md` to mention which Birko subset the project uses (mirrors user feedback `feedback_update_docs.md`).

## Next steps after scaffolding

Once this skill finishes, the consumer solution has a working aggregator + empty app project but no business code yet. Natural follow-ups:

- **Web UI shape** — use [[new-birko-web-page]] to add the first entity page (list/detail/CRUD) on top of `Birko.Web.Shell`. That skill assumes the shell is already scaffolded (i.e. that *this* skill has run) and walks through defining a `ModuleManifest`, wiring routes via `buildModuleRoutes()`, and registering the page class.
- **Any shape** — use [[tdd]] to drive the first feature (the project's xUnit + FluentAssertions test stack is already wired).

Skills that do **not** apply to a consumer scaffold (they extend Birko.Framework itself, not consumers): [[new-birko-subproject]], [[new-store-backend]], [[new-birko-web-component]], [[verify-birko-conventions]].
