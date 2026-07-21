---
name: verify-birko-conventions
description: Lint the staged or current diff against the conventions documented in `Birko.Framework/CLAUDE.md`. Use when the user says "verify conventions", "check birko rules", "lint pred commitom", "skontroluj zmeny", "are my changes following birko conventions", or before any commit on Birko.Framework. Catches nullable-warning regressions (CS8600–CS8625), concrete stores overriding public CRUD instead of `*Core`, missing tests for new public methods, hard-coded paths instead of `$(BirkoSrc)`, `RemoteSettings` constructed inline instead of via `base.SetSettings()`, missing `Recent Updates` entries for non-trivial changes, missing `CLAUDE.md` / `README.md` / `License.md` / `.gitignore` in new project directories, missing registrations in `.slnx` / `.code-workspace` / `Birko.Framework.csproj`, and new projects missing from the documentation index (`README.md` project table / `CLAUDE-projects.md` / `docs/`).
---

# Birko Framework — Convention Verifier

A pre-commit / post-change lint that enforces the rules listed in:

- `C:\Source\Birko\Framework\Birko.Framework\CLAUDE.md` § "Conventions" and § "Code Style"
- `C:\Source\Birko\Framework\Birko.Framework\CLAUDE-maintenance.md` § "New Project Checklist" and § "Solution & Workspace Registration"

Run this skill **before** every commit on Birko.Framework. It catches violations that the C# compiler doesn't surface (e.g. overriding `CreateAsync` instead of `CreateCoreAsync` compiles fine but bypasses lazy-init).

## Authoritative references — READ THESE FIRST when invoked

These files are the source of truth. If they change, the checks below should adapt to match — re-read on every invocation.

- `CLAUDE.md` § "Conventions" — the full convention list.
- `CLAUDE.md` § "Code Style" — guard clauses, no nullable warnings.
- `CLAUDE-maintenance.md` § "New Project Checklist" — required files in every project directory.
- `CLAUDE-maintenance.md` § "Solution & Workspace Registration" — required build-file registrations for new projects.
- `CLAUDE-maintenance.md` § "Documentation Index Registration" — required doc-index membership (README table / CLAUDE-projects / docs/) for new projects.
- `CLAUDE-maintenance.md` § "Test Requirements" — every new public method needs a test.
- `CLAUDE-maintenance.md` § "Health Check Requirements" — every external-service project needs a health check.

## What to lint

Run these checks in order. For each violation found, report:
- **File** (with path relative to `C:\Source\Birko\Framework\Birko.Framework\` where applicable)
- **Line number**
- **The rule violated**
- **Suggested fix** (one-line)

### 1. Nullable warnings (CS8600–CS8625)

Run `dotnet build C:\Source\Birko\Framework\Birko.Framework\Birko.Framework.slnx -warnaserror /p:TreatWarningsAsErrors=true` and report any of:
- CS8600 (converting null literal or possible null value to non-nullable type)
- CS8601 (possible null reference assignment)
- CS8602 (dereference of a possibly null reference)
- CS8603 (possible null reference return)
- CS8604 (possible null reference argument)
- CS8605 (unboxing a possibly null value)
- CS8618 (non-nullable field/property uninitialized)
- CS8625 (cannot convert null literal to non-nullable reference type)

These are forbidden per CLAUDE.md § "Code Style" — fix with proper null checks, `?` annotations, or `!` only when provably safe.

### 2. Store classes overriding public CRUD instead of `*Core`

Per CLAUDE.md § "Conventions": *Concrete stores override `protected *Core` methods, **NOT** the public CRUD methods.*

For every changed `.cs` file in `Stores/` directories, grep for:

```regex
public\s+override\s+(Task<.+>\s+)?(Create|Read|Update|Delete|Count)Async?\b
```

If found, report: "Concrete store `X` overrides public `Y` — should override `YCore` instead. The base class wraps the public method with `EnsureInitialized()` and dispatches to `*Core`; overriding the public method bypasses lazy-init."

Exception: classes whose name ends in `AbstractStore` / `AbstractBulkStore` / `AbstractAsyncStore` / `AbstractAsyncBulkStore` — those ARE the base classes and legitimately define the public method.

### 3. `RemoteSettings` constructed inline instead of via `base.SetSettings()`

Per CLAUDE.md § "Conventions": *`RemoteSettings` should be passed via `base.SetSettings()`, not constructed inline.*

Grep changed `.cs` files for `new RemoteSettings\b` outside of `base.SetSettings(new RemoteSettings…)`. Report violations.

### 4. Hard-coded paths to `Birko.*` projects

Per CLAUDE.md § "Usage in Consumer Solutions": *Use `$(BirkoSrc)` … for all `Import Project="…\Birko.X\Birko.X.projitems"` paths instead of hard-coded absolutes.*

Grep changed `.csproj` / `.projitems` files for `<Import Project="C:\\` or `<Import Project="\\.\\\.\\Birko` (relative-but-fragile) or any literal path containing `C:\Source\Birko\Framework\Birko.` outside of doc files. Report violations and suggest `$(BirkoSrc)\Birko.X\…`.

### 5. New public methods without tests

Per CLAUDE.md § "Testing": *Every new public functionality must have corresponding tests in `Birko.{ProjectName}.Tests`.*

For each new `public` method or class added in the diff (use `git diff --unified=0` and parse hunks):
- Identify the source project (folder it lives in).
- Check whether `Birko.{ProjectName}.Tests` exists.
- Check whether any file in that test project mentions the new method's name in a test method.

If no mention found, report: "Public method `Foo.Bar()` added in `Birko.X` has no test reference in `Birko.X.Tests`."

Note: this is a heuristic. False positives are possible (e.g. method tested through a higher-level call). The point is to **prompt the developer to confirm**, not to block.

### 6. New project directory missing required files

Per CLAUDE-maintenance.md § "New Project Checklist": every project dir must have `License.md`, `README.md`, `CLAUDE.md`, `.gitignore`.

For every new directory under `C:\Source\Birko\Framework\Birko.*\` introduced in the diff, verify all four files exist. Report missing files.

Also check GUID requirements: if a new `.shproj` or `.projitems` was added, verify:
- `ProjectGuid` / `SharedGUID` is a valid hex-only GUID (`0-9a-f`, format `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`).
- The two GUIDs in `.shproj` and `.projitems` match.

### 7. New project missing registration

Per CLAUDE-maintenance.md § "Solution & Workspace Registration": new projects must be registered in `Birko.Framework.slnx`, `Birko.Framework.code-workspace`, and `Birko.Framework.csproj`.

For every new `.shproj` or `.csproj` in the diff:
- Check `Birko.Framework.slnx` for a `<Project Path="…\NewName.shproj" />` entry. Report missing.
- Check `Birko.Framework.code-workspace` for a folder entry referencing `../NewName`. Report missing.
- For shared projects (`.shproj`), check `Birko.Framework.csproj` for an `<Import Project="..\NewName\NewName.projitems" />` line. Report missing.

### 7b. New project missing from the documentation index

Per CLAUDE-maintenance.md § "Documentation Index Registration": a new non-test `.shproj` project must appear in **all three** doc-index locations, not just the build files. Build-file registration (check #7) makes it compile; this makes it discoverable. This is the exact gap that let `Birko.EventBus.Tenant` ship undocumented.

For every new `.shproj` in the diff (excluding `.Tests`, `.ViewModel`, `.Views` companions — those inherit the parent's docs, but verify the parent is indexed):

- **`README.md`** — grep for a table row containing the project name (`| Birko.X |`). Report missing.
- **`CLAUDE-projects.md`** — grep for a bullet containing the project name. Report missing.
- **`docs/*.md`** — grep the whole `docs/` folder for the project name; it should appear on the topic page for its area (e.g. a new `Birko.Workflow.X` in `docs/workflow.md`). Report missing, and suggest the likely topic page by matching the project's second name segment (`Birko.Workflow.X` → `docs/workflow.md`, `Birko.EventBus.X` → `docs/event-bus.md`).

Quick full-repo drift sweep (catches pre-existing gaps, not just the current diff) — run from `C:\Source\Birko\Framework`:

```bash
cat Birko.Framework/README.md Birko.Framework/CLAUDE-projects.md Birko.Framework/CLAUDE.md Birko.Framework/docs/*.md > /tmp/alldocs.txt
for d in Birko.*/; do d="${d%/}"
  case "$d" in *.Tests|*.ViewModel|*.Views) continue;; esac
  { ls "$d"/*.shproj >/dev/null 2>&1 || ls "$d"/*.projitems >/dev/null 2>&1; } || continue
  grep -qF "$d" /tmp/alldocs.txt || echo "UNDOCUMENTED: $d"
done
```

Report any `UNDOCUMENTED:` line as a blocker.

### 8. External-service project missing health check

Per CLAUDE-maintenance.md § "Health Check Requirements": every project that connects to an external service must ship a health check.

If a new project under `Birko.Data.*` / `Birko.Caching.*` / `Birko.Communication.*` / `Birko.Storage.*` / `Birko.Workflow.*` / `Birko.BackgroundJobs.*` is added (excluding `.Tests`, `.ViewModel`, `.Views`, `.Core`), check that `Birko.Health.*` contains an `IHealthCheck` referencing it.

### 9. Significant change with no `Recent Updates` entry

If the diff touches **5+ files** OR contains a new project OR contains a new public interface, check whether root `CLAUDE.md` § "Recent Updates" was updated in the same diff. If not, report: "Significant change has no `Recent Updates` entry. Add one with the format `### Title (YYYY-MM-DD)`."

### 10. Guard-clause style (light touch)

Per CLAUDE.md § "Code Style": *Use early returns instead of wrapping entire method bodies in if blocks.*

This is harder to lint statically. As a heuristic, grep new methods for:

```csharp
if\s*\(\s*\w+\s*!=\s*null\s*\)\s*\{(?:[^}]*\{[^}]*\}[^}]*)*\}\s*$
```

(a method whose body is one big `if (x != null) { … }` with no else). Report as a suggestion, not a violation.

## Output format

Group findings by severity:

- **🛑 Blockers** — nullable warnings, missing `*Core` overrides, missing required files in new projects, missing build-file registrations, new projects missing from the documentation index.
- **⚠ Warnings** — missing tests for new public methods, hard-coded paths, missing health check.
- **💡 Suggestions** — missing `Recent Updates` entry, guard-clause violations.

Sample output:

```
🛑 Blocker — C:\Source\Birko\Framework\Birko.Data.Foo\Stores\FooStore.cs:42
   Concrete store overrides public CreateAsync — should override CreateCoreAsync instead.
   Fix: rename CreateAsync to CreateCoreAsync; remove the `Init()` call (base class handles it).

⚠ Warning — C:\Source\Birko\Framework\Birko.Data.Foo\Stores\FooStore.cs:118
   Public method BulkUpsertAsync added with no test reference in Birko.Data.Foo.Tests.
   Fix: add BulkUpsertAsync_RoundTrip to BulkOperationsTests.cs.

💡 Suggestion — C:\Source\Birko\Framework\Birko.Framework\CLAUDE.md
   Diff touches 7 files but no Recent Updates entry was added.
   Fix: prepend a `### Birko.Data.Foo — bulk upsert (YYYY-MM-DD)` entry.
```

If no findings: report `✅ All Birko conventions satisfied.`

## What this skill does NOT do

- It does not auto-fix violations. Findings are advisory; the developer applies fixes.
- It does not block git commit hooks. Wire a hook separately if you want enforcement (see `update-config` for hooks setup).
- It does not run integration tests. It only lints; run `dotnet test` separately.
- It does not check semantic correctness of code changes — only convention adherence.
