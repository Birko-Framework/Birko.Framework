---
id: TASK-234
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-229, TASK-230]
findings: []
pr: null
github-issue: null
jira-key: null
---

# 38 more shared projects use an external package they never declare

## Context

[[TASK-229]] settled the convention — a shared project declares its own external dependency, floating
within its major, in the CPM-compatible dual form — and applied it to the 8 storage backends plus 2 that
the float exposed. [[TASK-230]] added 3 more, each surfaced by an advisory rather than by design. **The
pattern is family-wide and those 13 were the ones something happened to point at.**

**Measured 2026-08-17 across all 171 `.projitems`: 38 still declare nothing they need.**

| Package | Projects |
|---|---|
| `NEST` | 7 |
| `MongoDB.Driver` | 5 |
| `StackExchange.Redis` | 5 |
| `Microsoft.AspNetCore.App` (**FrameworkReference**) | 4 |
| `Microsoft.Azure.Cosmos` | 4 |
| `RavenDB.Client` | 4 |
| `Grpc.Net.Client` / `Grpc.AspNetCore` | 2 |
| `Microsoft.IdentityModel.Tokens` | 2 |
| `InfluxDB.Client`, `Npgsql`, `MQTTnet`, `System.IdentityModel.Tokens.Jwt`, `Newtonsoft.Json`, `protobuf-net`, `YamlDotNet` | 1 each |

**The count is refined, not raw.** A first pass reported 43 by treating a namespace root as a package id,
which is wrong: `using Raven.*` is provided by `RavenDB.Client`, and `NpgsqlTypes` ships inside `Npgsql`.
The scan now maps namespace → package explicitly. Anyone re-running it should keep that distinction, or the
number drifts upward and the task looks bigger than it is.

**Why this matters, in the order the evidence arrived rather than by severity.**

- **A consumer discovers the dependency as a compile error**, with no indication which package to add. Every
  consumer rediscovers it independently.
- **A security fix costs N edits instead of one.** Measured in TASK-210: the same advisory class was one line
  in MongoDB (which declared) and ten repositories in SQLite (which did not). TASK-229's float then cleared
  29 advisory findings across 25 projects with **no per-project edit**, precisely because the declaration
  was in the shared project.
- **The dependency can vanish without anyone touching it.** Two of these were *found* that way:
  `Birko.Data.Repositories` and `Birko.Data.Tenant` compiled only because `RavenDB.Client ≤ 7.2.0` supplied
  their assemblies transitively, and floating that driver to 7.2.5 broke them. **Any of the 38 may be in the
  same position and nobody would know until an unrelated bump.**

## Approach

Not one sweep. The convention has four moving parts and each has a way to go wrong that has already been
measured:

1. **Declare in the dual CPM form.** A `Version`-carrying `PackageReference` makes a CPM consumer's solution
   unrestorable (`NU1008`) — it took out 14 projects across three consumers when TASK-229 landed.
2. **Add the matching `PackageVersion` to `Birko.Packages.props`** in the same change, or every CPM consumer
   breaks on `NU1010`.
3. **Remove the now-duplicate declaration from every dependent** in the same change. `NU1504` is a warning
   normally but an **error** under the `-warnaserror` that `verify-conventions` runs.
4. **`FrameworkReference` is stricter — a duplicate is a hard error** (`NETSDK1087`). The 4 rows above are
   `FrameworkReference` rows, so they will break every importer that also declares one until those are
   cleaned. That is what happened with `Birko.Data.Tenant`: 8 test projects, the sandbox and a consumer at
   once.

**Do it in batches by package, not all at once** — one package, its dependents, verify, commit. A single
sweep of 38 would produce a change set whose failures cannot be attributed.

### ⚠ Re-measured 2026-08-17 — **only 15 of the 38 may declare; for the other 23 declaring is the defect**

The count reproduces exactly (38 projects, 40 project×package pairs — two projects need two packages each,
which is why the table above sums to 40 while the headline says 38). What does *not* survive re-measurement
is the assumption that all 38 should end up with a declaration.

**23 of the 38 are satellites of a base `.projitems` that already declares the package**, and a shared
project cannot express "I depend on that other shared project" — the consumer imports both, so both
declarations land in one project file. Measured, not inferred: adding the Cosmos declaration to
`Birko.Data.CosmosDB.Views` and building `Birko.Data.CosmosDB.Views.Tests` gives

```
error NU1504: Duplicate 'PackageReference' items found. … Microsoft.Azure.Cosmos 3.*, Microsoft.Azure.Cosmos 3.*
```

— an **error** under the `-warnaserror` that `verify-conventions` runs, which is Approach point 3 arriving
from the other direction. Every satellite was checked against the test tree and **every one is imported
alongside its base**, which is structural rather than incidental: a `.Views` / `.ViewModel` / `Migrations.*`
/ `Sync.*` project extends the base store and cannot function without it.

| | Projects | Action |
|---|---|---|
| **Declare** (base or standalone) | **15** (17 pairs) | the actual work |
| **Do not declare** (base covers it) | **23** | record the pairing in the `.projitems`, per criterion 1 |

The 15 that declare:

| Package | Projects |
|---|---|
| `NEST` | `Birko.Data.ElasticSearch` — **the base itself is undeclared**, so its 6 satellites are blocked on it |
| `StackExchange.Redis` | `Birko.Redis` (base, undeclared) · `Birko.Health.Redis` (uses the driver directly and imports no base) |
| `Microsoft.AspNetCore.App` | `Birko.Communication.AspNetCore` · `Birko.Communication.WebSocket` · `Birko.Security.AspNetCore` · `Birko.Telemetry` |
| `Microsoft.IdentityModel.Tokens` | `Birko.Security.AspNetCore` · `Birko.Security.Jwt` |
| `System.IdentityModel.Tokens.Jwt` | `Birko.Security.Jwt` |
| `Grpc.Net.Client` / `Grpc.AspNetCore` | `Birko.Communication.gRPC` · `Birko.Communication.gRPC.Server` |
| `InfluxDB.Client` | `Birko.Data.Migrations.InfluxDB` — the one `Migrations.*` that does **not** import its base |
| `MQTTnet` · `Newtonsoft.Json` · `protobuf-net` · `YamlDotNet` | `Birko.MessageQueue.MQTT` · `.Serialization.Newtonsoft` · `.Serialization.Protobuf` · `.Serialization.Yaml` |

**`Birko.Data.ElasticSearch` and `Birko.Redis` declare nothing at all**, which contradicts TASK-229's
summary that "all 8 storage backends declare their own driver" — those two were not among its 8. They are
the highest-value items here, because each unblocks a family (6 and 3 satellites respectively).

**Order follows from that**: base first, then record its satellites in the same batch. Doing a satellite
first produces either a duplicate or a declaration that has to be taken out again.

**Ordering caveat for the `FrameworkReference` batch**: `NETSDK1087` is a hard error rather than NU1504's
warning-promoted-to-error, so those 4 break every importer that also declares one, in the same change —
Approach point 4 already says so, and it is the batch to do last, not first.

## Acceptance criteria

- [ ] Each of the 38 either declares its dependency, or records why it should not (a genuinely
      consumer-supplied choice is a legitimate answer — say so rather than declaring blindly).
      **Re-measured: that split is 15 declare / 23 record** — see the boxed section above. For the 23, the
      record goes in the `.projitems` itself, naming the base whose declaration covers it, because a
      comment in a task file is not where the next person looks
- [ ] Every declaration is in the dual `$(ManagePackageVersionsCentrally)` form, with its `PackageVersion`
      added to `Birko.Packages.props` in the same change
- [ ] Dependents' duplicate declarations removed in the same change as the declaration that duplicates them
- [ ] **The build sweep matches any `error <CODE>`, not a hand-listed set of prefixes.** TASK-229 reported
      "166 of 166 clean" while 10 projects were failing, because its pattern omitted `NETSDK`
- [ ] The sweep covers `Consumers/` as well as `Framework.Tests/`. `Birko.Sandbox` caught two defects in
      this thread that no test project could, because it consumes the framework through `$(BirkoSrc)` and an
      aggregator — the path every real consumer takes
- [ ] **Scope every cleanup by ownership, not by glob.** A `Consumers/*` glob edited
      `FisData.Stock.Angular`, which held substantial uncommitted work. Birko-owned consumers
      (`Birko.Sandbox`, `Birko.Xaml.Gallery`, `Birko.Web.Playground`) are fair game; the rest are not
- [ ] `audit-dependencies.ps1` reports no *new* findings, and no project newly **unauditable**

## Batches

### ✅ Batch 1 — `NEST` (7 projects), 2026-08-17

`Birko.Data.ElasticSearch` declares `NEST 7.*` in the dual form; the 6 satellites record the pairing in
their own `.projitems` rather than declaring. Duplicates removed from 7 test projects and from
`Birko.Sandbox` (Birko-owned). **187 tests green** across all 7 ElasticSearch suites.

Both halves proven rather than asserted:

- **The CPM half was measured with a synthetic consumer**, because no Birko-owned consumer uses CPM — the
  three that do are off-limits, so "it works under CPM" would otherwise have been an untested claim about
  the exact thing TASK-229 got wrong. A throwaway project with `ManagePackageVersionsCentrally=true`
  importing `Birko.Packages.props` builds, and resolves `NEST [7.*, )`. Removing the `PackageVersion` line
  and rebuilding gives `error NU1010`, so the check can fail.
- **The duplicate half**: NU1504 is a **warning** on a plain build and an **error** only under
  `-warnaserror`. That matters for scoping — `Affiliate` and `Symbio` both import
  `Birko.Data.ElasticSearch.projitems` *and* declare `NEST` themselves, and they are off-limits, so they
  now carry a warning until their owners remove it. A warning, not a break; had it been an error this batch
  would have needed the consumer-warning treatment of [[TASK-235]] first.

Two things that cost time and will recur in later batches:

- **A stale `obj/` made the Sandbox report `CS0579` eight times** and look like this change had broken it.
  It had not — cleaning `Birko.Framework/obj` as well as the outer `obj` cleared it. When a `.projitems`
  changes, clean the aggregator's *inner* obj too before believing a failure.
- **`Birko.Data.ElasticSearch.Tests` reported 12 errors on its first `-warnaserror` build and 0 on the
  next**, with no edit in between — a restore that had not yet picked up the changed `.projitems`. Build
  twice before recording a number.

**Audit**: `dotnet list package --vulnerable --include-transitive` over the 8 affected projects —
**0 findings, 0 unauditable**. Scoped to the blast radius rather than the whole family on purpose:
`audit-dependencies.ps1` restores every `.csproj` under `Framework.Tests` and `Consumers` (~200 of them), so
it is a task-level gate, not a batch-level one. Run it once at the end, not once per batch. Note the float
`7.*` resolves to the same 7.17.5 the pins named, because that is the last 7.x release — this batch changes
no resolved version anywhere.

**Left alone deliberately, and worth knowing about**: the same 8 project files also pin
`Elasticsearch.Net 7.17.5`, which is NEST's *own* dependency. It is not a duplicate of anything declared
here, so it is out of this task's scope — but a pinned transitive sitting beside a floating `NEST 7.*` is a
skew waiting for the next bump. Inert today only because 7.17.5 is the last 7.x release.

Spawned [[TASK-238]]: the sweep's `-warnaserror` surfaced five `Birko.Data.Sync.*` projitems carrying a
`ProjectReference` to another `.projitems`, which MSBuild cannot honour (`MSB9008`). Pre-existing and inert,
but it is noise in every remaining batch's sweep.

### ✅ Batch 2 — `StackExchange.Redis` (4 of 5 projects), 2026-08-17

`Birko.Redis` declares `StackExchange.Redis 2.*`; `BackgroundJobs.Redis`, `Caching.Redis` and
`MessageQueue.Redis` record the pairing. Duplicates removed from 4 test projects and the Sandbox. **130
tests green** across 5 suites, run against a **live Redis 7** — including TASK-237's leader-election tests,
which is the point of running them here rather than trusting an offline build.

**Unlike batch 1, this float actually moved a version: 2.8.24 / 2.8.41 → 2.13.17.** It broke two things,
which is precisely the risk the task's own Context describes ("the dependency can vanish without anyone
touching it… any of the 38 may be in the same position and nobody would know until an unrelated bump"). Both
are now fixed:

- **`RedisCache.SetAsync` stopped compiling.** 2.9 added
  `StringSetAsync(key, value, Expiration, ValueCondition, CommandFlags)` with optional trailing parameters,
  so a *three-argument* call now binds **that** overload — and `TimeSpan?` does not convert to `Expiration`
  (only a non-nullable `TimeSpan` does). Fixed by naming `When.Always`, which was the old default, so
  semantics are unchanged and it compiles against 2.8 and 2.13 alike.
- **Seven `Birko.MessageQueue.Redis` tests failed with the production code untouched.** `StreamAddAsync`
  and `StreamReadGroupAsync` each gained an all-optional overload (`limit` + `StreamTrimMode`;
  `claimMinIdleTime`), and `maxLength` widened `int?` → `long?`. Production bound the new overloads
  silently and correctly; the **Moq setups still named the old ones**, so the callbacks never fired and the
  captured value was null. A mock names an overload, so it is a version-coupled assertion — worth knowing
  before the next float moves a driver under a mocked API.

**This is the strongest evidence so far that the float is worth its cost.** Nobody edited
`Birko.Caching.Redis` or `Birko.MessageQueue.Redis`; declaring a dependency in a *different* project
surfaced a latent incompatibility in both. Pinning would have hidden it until an advisory forced the bump.

### ⚖ Batch 2 outcome — **the wrapping project owns the driver; `Birko.Health.Redis` is the one carve-out**

Settled 2026-08-17 after two reversals, which are recorded because the second one only became decidable
once a premise was measured.

**The framing that settled it** (the user's): *Birko is the unifying middleware between necessary libraries
— database drivers, cache providers — and the consumer's code.* If that is what the framework is, then the
shared project owning its driver **is the product**: a consumer imports `Birko.Data.MongoDB` and never
learns the package is `MongoDB.Driver 3.*`. Consumer-supplied inverts that and also discards TASK-229's
measured benefit — one float cleared **29 advisory findings across 25 projects with no per-project edit**,
precisely because the declaration sat in the shared project.

**The objection, and the measurement that answered it.** The concern was that a consumer needs only a
*subset* of the framework, so ownership would push packages onto projects that do not want them. It does
not: a `PackageReference` inside a `.projitems` materialises **only in a project that imports that
`.projitems`**. Measured from the resolved assets files:

| Consumer imports | Libraries resolved | Drivers pulled in |
|---|---|---|
| `Birko.Data.ElasticSearch` | 24 | NEST only |
| `Birko.Redis` | 25 | StackExchange.Redis only |
| `Birko.Data.MongoDB` | 33 | MongoDB.Driver only |
| `Birko.Health.Redis` | 28 | StackExchange.Redis only |

**Ownership is the subset model**, not a threat to it. Consumer-supplied does not shrink the subset; it only
makes the consumer name each driver by hand.

**What is genuinely subset-hostile is coupling one `.projitems` to another** — and that is why the obvious
"fix the layering" answer was rejected. `Birko.Health.Redis` bypasses `Birko.Redis` and talks to the driver
directly, which looks like a layering smell (and is one: `RedisConnectionManager` exposes `GetDatabase()`
but no multiplexer, so the health check *could not* go through it). Making it depend on `Birko.Redis` would
close the family — and drag `Configuration`, `Data.Core`, `Data.Stores`, `Contracts` and `Time` into a
consumer that wanted one health check. **A design fix that violates the constraint is not a fix.**

**Settled rule:**
- **The project that wraps a driver owns it** — declared, floating within its major, dual CPM form.
- **Siblings built on it do not re-declare** — they record the pairing (NU1504 otherwise).
- **A project that uses the same driver independently documents it as consumer-supplied** — the narrowest
  possible carve-out, costing a standalone consumer one line and everyone else nothing.
- **A `FrameworkReference` is never owned.** `Microsoft.AspNetCore.App` is not a driver Birko wraps; the
  host has it from `Microsoft.NET.Sdk.Web`. All four projects will document it. Not an exception — a
  different category.
- **NuGet packages dissolve the whole class of problem** and remain the long-term answer. What is being
  written into these `.projitems` now *is* a package's dependency list, so none of it is wasted.

**Final state**: `Birko.Redis` declares `StackExchange.Redis 2.*`; `BackgroundJobs.Redis`, `Caching.Redis`
and `MessageQueue.Redis` record the pairing; `Birko.Health.Redis` documents the standalone case; its two
test projects keep their own `2.*` declaration; four other test projects and the Sandbox drop theirs.
**221 tests green** across 6 suites against a live Redis 7; Sandbox and the CPM probe both build clean.

The case that forced the carve-out — `Birko.Health.Redis`:

It uses `StackExchange.Redis` directly and depends on **nothing** from `Birko.Redis` — so it is not a
satellite. But `Birko.Sandbox` imports both, so if both declare, that project file gets two
`PackageReference` items and NU1504 fires, which the sweep's `-warnaserror` turns into an error. The two
available answers both cost something real, and the choice sets the rule for every later batch:

- **(A) Couple it** — record the pairing and require consumers to import `Birko.Redis`. **Measured cost:
  five extra shared projects**, not one. `Birko.Redis` is two files but `RedisSettings` extends
  `RemoteSettings` and implements `ILoadable`, so it drags `Birko.Configuration`, `Birko.Data.Core`,
  `Birko.Data.Stores`, `Birko.Contracts` and `Birko.Time` behind it. Tried it: both Health test projects
  failed with `CS0246: RemoteSettings could not be found` until the whole chain was added. A consumer that
  wants one health check takes the settings hierarchy with it.
- **(B) Let it declare** — accept a benign NU1504 wherever both are imported, and `NoWarn` it in the
  Sandbox. The duplicate is harmless (identical `Include` and version; NuGet's warning exists for
  *differing* versions), but suppressing a code in the sweep is the shape that produced "166 of 166 clean"
  while 10 projects were broken.

**Chosen: (C) for this project only** — `Birko.Health.Redis` documents the package and declares nothing,
while `Birko.Redis` owns it. (A) was rejected on the measured five-project import chain, (B) on the
suppression.

**Two reversals happened before that landed, and the sequence is the lesson.** The batch first shipped with
`Birko.Redis` owning the driver; it was then reverted to consumer-supplied across the whole family on the
reading that a package with any outside user cannot be owned; it was then restored, because the premise
underneath the reversal — that ownership pushes packages onto consumers wanting only a subset — turned out
to be false when measured. **The decision was reversed twice and only the third state rests on a
measurement.** Both earlier states were internally coherent; that is exactly why neither was safe to keep.

### ✅ Batch 3 — `MongoDB.Driver` (5 projects), 2026-08-17

**Documentation only.** `Birko.Data.MongoDB` already declared the driver (TASK-229) and no test project or
consumer declared it, so there was nothing to remove — the five satellites simply had no comment saying the
absence was deliberate. That is the whole content of the batch, and it is worth doing: the next audit reads
an undocumented absence as the defect and adds a declaration that would collide.

**135 tests green** across 7 MongoDB suites against a **live MongoDB 7**, `-warnaserror` clean.

Two things fell out of it:

- **[[TASK-238]] closed on the way through**, because it failed this batch's sweep — the second batch in a
  row. **Seven projects carried the dead `ProjectReference`, not the five it was filed with**:
  `Birko.Data.Sync.Sql` and `.Xml` were missed because the original survey greped only the ones a failing
  build had named. All seven Sync suites now build `-warnaserror` clean; 54 tests green.
- **`--` is illegal inside an XML comment**, and the replacement text for TASK-238 used it as a dash. Every
  one of the seven `.projitems` failed to load with `MSB4024: An XML comment cannot contain '--'`. The
  earlier batches' comments used `—` and were fine, which is exactly why it was not noticed sooner. Caught
  by the build, not by review.

**Filed nothing for it, but recorded here**: `Birko.Data.Sync.RavenDB.Tests` emits
`NU1510: PackageReference Microsoft.Extensions.DependencyInjection.Abstractions will not be pruned — this
package is automatically available`. Pre-existing, unrelated to this batch, and the *opposite* defect to
this task's — an over-declaration rather than a missing one. Worth a sweep of its own once the batches are
done, because `Birko.Packages.props` carries a `PackageVersion` for that same package
(`Birko.Data.Repositories`), so the shared project may be declaring something net10 provides.

## Out of scope

- The 13 already done — [[TASK-229]] (10) and [[TASK-230]] (3).
- Shipping the backends as real NuGet packages: deferred by the user until the libraries stabilise. This
  work is forward-compatible with it, since a package's dependency list is exactly what gets declared here.
- Advisories. [[TASK-230]] closed the framework-owned ones; a *new* one arriving mid-task is
  `audit-dependencies.ps1`'s job to report, not this task's to chase.

## Human test plan

N/A — restore, build and `dotnet list package --vulnerable` are mechanical.

## Implementation plan

_Populated by `/tasks plan TASK-234` — leave empty until then._
