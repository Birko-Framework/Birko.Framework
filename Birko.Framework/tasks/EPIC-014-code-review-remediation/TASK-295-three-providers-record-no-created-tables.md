---
id: TASK-295
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-243, TASK-286, TASK-287, TASK-288, TASK-290, TASK-293]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MySQL, Birko.Data.SQL.MSSql, Birko.Data.TimescaleDB]
---

# The escape and heal apparatus is SQLite-only: three providers record no created tables

## Measured

`AbstractConnector.RecordTableCreated` is called from exactly **one** place — the **base**
`AbstractConnector.CreateTable(string, IEnumerable<string>)` (`AbstractConnector_Create.cs:107`). And:

| provider | overrides `CreateTable(string, IEnumerable<string>)` | calls `RecordTableCreated` |
|---|---|---|
| SQLite | no | **yes**, via the base |
| PostgreSQL | yes | **no** |
| MySQL | yes | **no** |
| SQL Server | yes | **no** |
| TimescaleDB | yes, and calls `base.` — which is *PostgreSQL's* override | **no** |

Verified on live PostgreSQL 16, MySQL 8.4 and SQL Server 2022: after
`connector.CreateTable(new[] { typeof(Probe) })`, `TablesCreated` is **empty**; then, with the table
dropped, a count answers `0`, `SchemaEscapes` is **empty** and `SchemaGeneration` stays at **0**. Each
provider suite carries that as `TASK295_this_provider_records_no_created_tables_so_the_anomaly_is_unobservable_here`.

## What is therefore inert on three of four providers

Everything that hangs off `TablesCreated`, which since TASK-288 is a chain rather than a diagnostic:

- **TASK-286's annotation** — always the benign branch, *"this connector has NO recorded CREATE TABLE for
  any table named in the statement"*, even for a table it created seconds earlier;
- **TASK-287's `SchemaEscapes` / `OnSchemaEscapeDetected`** — never records, never fires. A host that
  subscribes gets silence that is indistinguishable from "the condition never happened";
- **TASK-288's healing** — `SchemaGeneration` never moves, so a store whose table vanished beneath it
  keeps its remembered `_initialized` and every write throws **until the process restarts**. That is the
  consumer-reported defect TASK-288 closed, still open on PostgreSQL, MySQL and SQL Server.

⚠ **This matters now rather than eventually.** Consumer Symbio deploys on SQLite today, which is the only
provider where any of this works — and TASK-256 records that a move to PostgreSQL is expected. On the day
that happens, all three land at once, silently, and the instrumentation built to investigate them goes
dark with them.

## Fifth instance of a rule this repo has already written down

§ TASK-243: *"a funnel with four overrides is not a funnel"* — after TASK-215 (the base's own wrappers),
TASK-242 (store `*Core` overrides), TASK-243 (the DDL emitters) and TASK-245 (the async twin nothing
calls). The instruction that came with it is *"when introducing a funnel, grep `override` on every method
that reaches it before believing the wiring, and confirm with a revert rather than a read"*. TASK-286
introduced a funnel and did not.

## The shape of the fix, and the one to avoid

⚠ **Do not add `RecordTableCreated(name)` to the three overrides.** That is a fourth copy of the rule and
it re-arms this exact defect for the next provider — which is how this file already has five instances.
Measure and choose between:

1. **Record in the dispatcher, not the leaf.** `CreateTable(IDictionary<string, IEnumerable<AbstractField>>)`
   loops and calls the virtual leaf; recording there covers every override by construction.
   ⚠ Cost, and it needs measuring: the leaf has one external direct caller,
   `Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:281`, which would stop being recorded. It is not
   recorded on three providers today either, so this is not a regression on them — but it is one on SQLite.
2. **Template-method the leaf**, matching the framework's own documented `*Core` convention: a non-virtual
   public `CreateTable(string, IEnumerable<string>)` that calls a `protected virtual CreateTableCore` and
   then records. Covers every caller including the migration builder, and a provider override cannot bypass
   it.
   ⚠ Cost: `CreateTable(string, IEnumerable<string>)` is `public virtual`, so any **consumer** override
   breaks loudly (`CS0506`/`CS0115`). Measure that across the 16 consumer repos first — loud is this
   repo's preference, but the count has to be known, and § TASK-278 records that changing a `public
   virtual`'s shape silently orphans overrides when the change is additive rather than a removal.

Whichever is chosen, the assertion in each provider suite **inverts** rather than being deleted — the
before/after pair on one test is the record, as TASK-277 did to TASK-244's pin and TASK-265 did to
TASK-257's.

## Acceptance

- [ ] `TablesCreated` is populated on all four providers plus TimescaleDB, asserted live per provider.
- [ ] The recording cannot be bypassed by adding a provider override — demonstrated, not asserted in
      prose (e.g. a test provider that overrides the emitter and still records).
- [ ] TASK-288's healing is asserted **live on PostgreSQL and SQL Server**, not only on SQLite: table
      dropped beneath an initialised store, first write reports, second write heals.
- [ ] TASK-286's annotation shows the anomalous branch on those providers.
- [ ] `SqlSchemaBuilder`'s direct call is either still recorded or the loss is stated with its reason.
- [ ] The three provider pins from [[TASK-293]] are inverted, in the same commit.
- [ ] Mutation-proven per provider.

## Out of scope

- [[TASK-293]] — which table an escape is about. Already fixed and independent: it reads the provider's
  error, not the record. It is what made this defect visible.
- [[TASK-290]] — the mechanism behind the consumer's escapes. This changes what its instrument can see on
  three providers, which is why it is P1 rather than a cleanup.
