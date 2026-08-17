---
id: TASK-231
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: review
priority: P1
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-232]
findings: []
pr: 68a1bcd (Birko.EventBus.Outbox.SQL.Tests, new repo) + a7a0d87 (Birko.EventBus.Outbox.SQL)
github-issue: null
jira-key: null
---

# `Birko.EventBus.Outbox.SQL` shipped complete but registered nowhere — unbuilt, untested, invisible

## Context

Raised by the user 2026-08-17 and confirmed by a sweep of all **341** projects on disk against the two
machine-readable registries. `Birko.EventBus.Outbox.SQL` is a real, finished project — its own git repo
(2 commits), `.shproj` + `.projitems`, `CLAUDE.md`, `README.md`, `License.md`,
`Models/OutboxEntryModel.cs`, and `SqlOutboxStore<DB> : IOutboxStore`. The contract is implemented
correctly; nothing is wrong with the code.

**It is simply not wired into anything**, and it is the *only* project of 341 in that state:

| Registry | State | Consequence |
|---|---|---|
| `Birko.Framework.slnx` | **missing** | invisible in the solution |
| `Birko.Framework.code-workspace` | **missing** | invisible in the editor workspace |
| `Birko.Sandbox` build-validation aggregator | **missing** — it imports `Birko.EventBus.Outbox` at line 287 but not `.SQL` | **never compiled by anything** |
| `Birko.EventBus.Outbox.SQL.Tests` | **does not exist** — only `Birko.EventBus.Outbox.Tests` | **no test covers it** |
| `.gitignore` | **missing** (New Project Checklist item 4) | `bin/` and `obj/` can be committed |

The third row is the one that makes this P1 rather than paperwork. Nothing in the family compiles this
project, so it is not merely undocumented — **it has no build validation at all**, and a change to
`IOutboxStore` or to `Birko.Data.SQL` would break it with nothing to notice. The fourth row compounds it:
`CLAUDE.md` § Testing requires that *"every new public functionality must have corresponding tests in
`Birko.{ProjectName}.Tests`"*, and this is a durable store — exactly the kind of thing whose round-trip
must be pinned.

**Directly relevant to work that just landed.** `Birko.EventBus.Outbox@bbd9389` added an `AddOutbox`
overload taking a store factory, and its own doc comment gives the reason: *"A SQL-backed store is
parameterised by settings and a connector type chosen from configuration, so it cannot be [activated
through the container]."* The overload was written **for this project**. So the framework has a
registration path aimed at a store that nothing builds and nothing tests.

**Why the checklist did not catch it.** `CLAUDE-maintenance.md` § *Solution & Workspace Registration*
covers the `.slnx` and the `.code-workspace` and is otherwise correct — it simply was not run. Two things
it does **not** mention at all, and which this task should add, are the build-validation aggregator and
the sibling test project.

**Not filed as a documentation gap.** `CLAUDE-projects.md` also omits it, but that file is prose with
shorthand (`**Birko.BackgroundJobs.SQL** / **.ElasticSearch** / …`) and grouped test listings, so it is
not machine-checkable and a "missing" verdict there is unreliable. Only the two structured registries and
the two build/test facts above are asserted here.

## Acceptance criteria

- [x] Registered in `Birko.Framework.slnx` under the **EventBus/** folder group, and in
      `Birko.Framework.code-workspace` with the `"EventBus / Birko.EventBus.Outbox.SQL"` name convention,
      sorted alphabetically
- [x] Imported by the `Birko.Sandbox` build-validation aggregator beside `Birko.EventBus.Outbox`, so it is
      actually compiled. **Verify by building it, not by reading the import** — and note the ordering
      dependency on [[TASK-228]] if the sandbox is still untracked when this runs
- [x] `Birko.EventBus.Outbox.SQL.Tests` exists and covers `SqlOutboxStore<DB>`'s round trip against a real
      SQLite file, in the manner of the other SQL-backed suites, not with hand-built connector objects
- [x] The `AddOutbox(storeFactory)` overload from `Birko.EventBus.Outbox@bbd9389` is exercised by at least
      one test that registers a `SqlOutboxStore` through it — the overload exists for this store and is
      currently unproven against it
- [x] `.gitignore` added, matching the sibling projects
- [x] `CLAUDE-maintenance.md` § *Solution & Workspace Registration* gains the two steps it is missing —
      import into the build-validation aggregator, and create the sibling `.Tests` project — since the
      existing checklist was followed and still let this through
- [x] The 341-project registration sweep is re-run and reports **zero** projects missing from `.slnx` or
      `.code-workspace`

## Out of scope

- `CLAUDE-projects.md` coverage. Its shorthand makes an automated verdict unreliable, and 137 apparent
  "gaps" there were measured to be an artefact of the matcher rather than real omissions. Making that file
  machine-checkable is its own decision, not this task's.
- Any change to `SqlOutboxStore`'s behaviour. The code is fine; it is the wiring that is missing.
- The `IJobLockProvider` asymmetry — [[TASK-232]].

## Outcome

**The wiring was the whole defect — the code was fine.** `Birko.EventBus.Outbox.SQL` compiled on the
**first ever attempt** by anything in the family. That is worth recording plainly, because the task's own
Context argued it "would have broken with nothing to notice": the risk was real but had not yet been
realised. Luck, not a guarantee.

**What landed.**

| | |
|---|---|
| `.slnx` | shared project under **EventBus/**, test project under **Tests/** |
| `.code-workspace` | `EventBus / …Outbox.SQL` **and** `Tests / …Outbox.SQL.Tests` — validated as JSON after editing |
| Build validation | `Birko.EventBus.Outbox.SQL.Tests` imports the `.projitems`; the Sandbox aggregator imports it too, and **still builds** |
| Tests | **17 / 17** — 10 round-trip against a real SQLite file, 7 on the DI overload |
| `.gitignore` | added to the source project *and* the new test project |
| Sweep | **342 of 342** registered, zero missing |

**Two findings from writing the tests, neither a defect.**

- **`SqlOutboxStore(SqlSettings)` cannot be used with SQLite at all.** `SqLiteSettings` descends from
  `PasswordSettings`, not `SqlSettings` — the documented settings chain hangs SQLite off the password
  branch because it has no host, port or user. So SQLite must use the pre-built-store constructor, which
  is exactly what that constructor's doc comment means by *"SQLite passes one in"*. The author knew;
  the test now pins it, with the reason, so the next reader does not treat it as an oversight.
- **`OutboxProcessor` cannot be resolved without the host registering an `IEventBus`.** My first test
  asserted it could, and that was my error rather than the code's — the outbox deliberately does not choose
  a transport. Both directions are now pinned: with a bus the processor constructs, without one resolution
  throws naming `IEventBus`. The second test exists so that a future change which silently registers a
  default bus is noticed rather than welcomed.

**The sweep caught my own miss.** After registering, the re-run still reported one project unregistered —
`…Outbox.SQL.Tests`, which I had added to the `.slnx` and forgotten in the workspace. The `.slnx` and the
`.code-workspace` are two independent lists and updating one is not updating the other; that is now a
sentence in the checklist, because it is the same class of mistake the task was filed for.

**Held at `review`, not `done`, for one reason.** The Sandbox aggregator import is real and verified by a
build, but `Birko.Sandbox` **is not a git repository** ([[TASK-228]]) — so that one edit is machine-local
and will not survive a clean clone. Every other criterion is committed. This flips to `done` when 228
lands and the import is versioned; the alternative was to claim a criterion that only holds on this disk.

## Human test plan

N/A — every criterion is mechanical: a project either appears in a registry, compiles, and has a passing
test suite, or it does not.

## Implementation plan

_Populated by `/tasks plan TASK-231` — leave empty until then._
