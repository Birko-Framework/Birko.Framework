---
id: TASK-145
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Nothing at the `GetUnwrappedStore` call sites says they strip every decorator

## Context

Filed from [[TASK-125]], whose `## Out of scope` says explicitly: *"Worth a doc note on
`GetUnwrappedStore` and the CLAUDE.md files, but it is a documentation task, not this defect — file
separately if it is not picked up here."* It was not picked up there.

`GetUnwrappedStore` walks `IStoreWrapper.GetInnerStore()` down to the innermost store. **70 call sites
across 21 data projects** resolve this way. 69 are the intentional escape hatch — the `XStore` /
`Connector` properties (`CosmosStore`, `ElasticSearchStore`, `MongoStore`, `RavenStore`, …) that exist so
callers can reach backend-native features a portable store cannot express. That design is deliberate and
load-bearing.

The 70th was [[TASK-125]]: an ordered `ReadOne` that used the hatch to serve a **portable read**, and so
returned the first matching row from any tenant, with soft-delete, localization and audit dropped by the
same call. It read like ordinary code at the call site, because nothing there says what unwrapping costs.

TASK-125 left one executable statement of the danger —
`GetUnwrappedStore_strips_the_tenant_wrapper_which_is_why_reading_through_Connector_leaked` in
`Birko.Data.SQL.ViewModel.Tests` — but a test in one project is not a warning anybody meets while writing
the 71st call site.

## Acceptance criteria

- [ ] `GetUnwrappedStore`'s own XML doc states that it drops **every** decorator (tenant, soft-delete,
      localization, audit) and that it is for backend-native capability, never for a portable read
- [ ] The warning reaches the `XStore` / `Connector` property docs, or a single shared note they all
      reference, so it is visible where the hatch is actually taken rather than only at its definition
- [ ] `Birko.Data.Stores/CLAUDE.md` — and any project CLAUDE.md documenting the hatch — carries the rule,
      naming [[TASK-125]] as the defect that proves it
- [ ] The wording **distinguishes the two cases** rather than discouraging the hatch generally. 69 of 70
      uses are correct; a note that reads as "don't do this" will be ignored by all of them, and an
      ignored warning is worse than none because it looks like coverage

## Out of scope

- Removing or narrowing the escape hatch itself — deliberate and load-bearing.
- Auditing the 69 call sites; [[TASK-125]] already measured them as correct.
- The async parity question ([[TASK-146]]).

## Human test plan

N/A — documentation only.

## Implementation plan

_Populated by `/tasks plan TASK-145` — leave empty until then._
