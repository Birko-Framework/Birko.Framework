---
id: TASK-231
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-232]
findings: []
pr: null
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

- [ ] Registered in `Birko.Framework.slnx` under the **EventBus/** folder group, and in
      `Birko.Framework.code-workspace` with the `"EventBus / Birko.EventBus.Outbox.SQL"` name convention,
      sorted alphabetically
- [ ] Imported by the `Birko.Sandbox` build-validation aggregator beside `Birko.EventBus.Outbox`, so it is
      actually compiled. **Verify by building it, not by reading the import** — and note the ordering
      dependency on [[TASK-228]] if the sandbox is still untracked when this runs
- [ ] `Birko.EventBus.Outbox.SQL.Tests` exists and covers `SqlOutboxStore<DB>`'s round trip against a real
      SQLite file, in the manner of the other SQL-backed suites, not with hand-built connector objects
- [ ] The `AddOutbox(storeFactory)` overload from `Birko.EventBus.Outbox@bbd9389` is exercised by at least
      one test that registers a `SqlOutboxStore` through it — the overload exists for this store and is
      currently unproven against it
- [ ] `.gitignore` added, matching the sibling projects
- [ ] `CLAUDE-maintenance.md` § *Solution & Workspace Registration* gains the two steps it is missing —
      import into the build-validation aggregator, and create the sibling `.Tests` project — since the
      existing checklist was followed and still let this through
- [ ] The 341-project registration sweep is re-run and reports **zero** projects missing from `.slnx` or
      `.code-workspace`

## Out of scope

- `CLAUDE-projects.md` coverage. Its shorthand makes an automated verdict unreliable, and 137 apparent
  "gaps" there were measured to be an artefact of the matcher rather than real omissions. Making that file
  machine-checkable is its own decision, not this task's.
- Any change to `SqlOutboxStore`'s behaviour. The code is fine; it is the wiring that is missing.
- The `IJobLockProvider` asymmetry — [[TASK-232]].

## Human test plan

N/A — every criterion is mechanical: a project either appears in a registry, compiles, and has a passing
test suite, or it does not.

## Implementation plan

_Populated by `/tasks plan TASK-231` — leave empty until then._
