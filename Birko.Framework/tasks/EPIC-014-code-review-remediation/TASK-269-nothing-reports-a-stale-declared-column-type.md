---
id: TASK-269
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-204, TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Nothing reports a column whose stored type no longer matches what the model declares

## Context — spawned by TASK-257, whose decision depends on an operator noticing

TASK-257 settled that the framework does **not** repair an existing database: `CreateTable` is guarded by
`IF NOT EXISTS` and schema-ensure never reconciles an existing table's columns, so a database created
before that fix keeps its `TEXT` columns and **keeps failing every string predicate afterwards**. The
remedy is a hand-run `ALTER TABLE … ALTER COLUMN … NVARCHAR(MAX)`.

That decision is only reasonable if somebody finds out. Today nobody does:

- The framework has **no schema-drift surface at all** — no equivalent of `IIndexManager` for column types.
- Consumer Symbio's `Symbio.DataAccess/Sql/SchemaDriftCheck.cs` compares column **presence** only, so a
  column of the wrong *type* reads as perfectly healthy.

So the failure mode is: an upgraded deployment starts throwing Msg 402 on every string predicate, and the
only signal is the exception at the call site. The blast radius for TASK-257 specifically is zero (no
deployment selects MSSql today), which is why it was allowed to close — but the gap is general and outlives
that particular fix. Any future column-type change has the same problem.

## Why it matters beyond TASK-257

This is the third member of a family the epic keeps rediscovering: a condition the framework *knows* about
and reports to nobody.

- TASK-204 made schema-ensure record an unbuildable index on `IndexCreationFailures` and raise
  `OnIndexCreationFailed` — **nothing in the tree subscribes**, so every index failure since has been
  silent. TASK-245, TASK-248 and TASK-257 each found real, long-standing index failures hiding behind it.
- The same is now true one level down, for the column type itself.

A report nobody reads is indistinguishable from no report, and this epic has paid for that three times.

## What to decide

1. **Where does the check live?** A framework-side probe (compare `DataBase.LoadTable`'s fields against the
   provider's catalogue — `sys.columns`, `information_schema.columns`, `pragma_table_info`) is reusable by
   every consumer; a consumer-side one only fixes Symbio. Framework-side is the obvious call, but it needs a
   home — an `ISchemaDriftCheck` beside `IIndexManager` is the natural shape.
2. **When does it run?** On demand (a diagnostic a host can call, or a health check — note
   `CLAUDE-maintenance.md` § *Health Check Requirements* already expects external-service projects to ship
   one), or at schema-ensure? Not at schema-ensure by default: it costs a catalogue round-trip on first use
   of every store.
3. **Does anything subscribe?** This is the part that actually matters, and the part TASK-204 got wrong.
   A new reporting channel with no consumer repeats the defect exactly. Decide the subscriber before the
   producer — and consider whether `IndexCreationFailures` should be surfaced through the same door, so
   there is one place a host asks "is my schema what my models think it is?".

## Acceptance criteria

- [ ] A decision recorded on all three questions above, with reasons.
- [ ] A drift check that detects, against a real server, a column whose stored type differs from the
      declared one — proven with the actual TASK-257 case: create a table with a `TEXT` column, point a
      model declaring an unlengthed `string` at it, and see the drift reported.
- [ ] **Something consumes the report** — a health check, a startup log line, or a diagnostic endpoint.
      A channel with no subscriber does not satisfy this criterion; that is the TASK-204 failure being
      repeated.
- [ ] Decide whether `IndexCreationFailures` joins the same surface, and record the answer either way.
- [ ] Proven able to fail.

## Out of scope

- Auto-repairing the drift. TASK-257 rejected auto-`ALTER` on schema-ensure (store init rewriting existing
  production columns is a quiet destructive write); this task is about *detection*, and it should not
  quietly acquire the remedy.
- The column typing itself — TASK-257 (strings), TASK-266 (binary/width), TASK-268 (`TimeOnly`).

## Human test plan

- [ ] A human confirms the drift report is actually visible where an operator would look (health endpoint,
      log, or diagnostic output) — the whole point of the task is that a person finds out, so a green
      automated assertion that the API returns a list is not sufficient on its own.
