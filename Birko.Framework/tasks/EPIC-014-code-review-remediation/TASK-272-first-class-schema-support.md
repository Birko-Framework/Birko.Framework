---
id: TASK-272
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-209, TASK-211, TASK-253, TASK-262]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MSSql, Birko.Data.SQL.PostgreSQL]
---

# An entity cannot say which schema it lives in

## Context — the design question TASK-262 deliberately did not answer

TASK-262 fixed a *regression*: `AbstractConnectorBase.QualifiedIdentifier` now splits a caller-supplied
`reporting.evts` on unquoted dots and quotes each part, so the TimescaleDB migration emitters reach a
schema-qualified object again. That covers a name a **caller** hands in as a string.

It does **not** give the framework a concept of a schema. Measured at that task's close:

- **No schema concept exists anywhere.** Not on `Attributes.Table` (which takes only `Name`), not on
  `Tables.Table` (`Name`, `Fields`, `Type`, `Indexes`), and not in any `Settings` class.
- So an entity cannot declare its schema, and every read and every DDL statement addresses the default schema
  of whatever connection the settings produced.

## Why this is table-and-connector, not one or the other

Established by measurement rather than preference:

- **Connector-only cannot express it.** A connector is cached process-wide per (type, settings id), so it can
  hold at most *one* schema. That is a **default / search path** — "everything lives in `reporting`" — which is
  a legitimate and *separate* feature. It cannot say "entity A is in `reporting`, entity B is in `public`".
- **Table-only cannot render it.** `Tables.Table` holds no connector, and quoting is provider-specific (`"`
  vs `` ` `` vs `[]`), as is whether schemas exist at all. A table rendering its own qualified name would have
  to hardcode ANSI quotes and break MySQL and MSSql.
- **The framework already has the seam.** `Table.GetSelectFields(…, Func<string,string>? quoteTable)` takes
  the quoting *in* as a delegate for exactly this reason (TASK-209). Follow that split: the table knows the
  name, the connector knows the dialect.

So the shape is: **identity on the table, rendering and capability on the connector.**

```
[Table("evts", Schema = "reporting")]      -> Tables.Table.Schema (nullable = provider default)

AbstractConnectorBase:
    SupportsSchemas                        -> capability, same family as SupportsTransactionalDdl,
                                              FoldsUnquotedIdentifiers, IsMissingTableException
    QualifiedIdentifier(schema, name)      -> one producer, per-part quoting (TASK-262 built the
                                              string-splitting half already)
```

## The per-provider reality, measured 2026-08-21

| Provider | `reporting.evts` means | True intra-database schemas |
|---|---|---|
| PostgreSQL 16.15 | schema `reporting` in the current DB (`public` and `reporting` both inside `birkoview`) | **yes**, default `public` |
| SQL Server 2022 | schema `reporting`, default `dbo` | **yes** |
| MySQL 8.4 | **a different database** — `CREATE SCHEMA reporting` created a sibling of `birkoview` | no; SCHEMA is a synonym for DATABASE |
| SQLite | nothing, beyond an `ATTACH`ed database prefix (`main.`) | no |

**So `SupportsSchemas` has two `true` and two `false` providers.** Per § Conventions that means the `false`
side must be asserted, or the capability is indistinguishable from an unconditional one and could be deleted
with no test noticing — the `FoldsUnquotedIdentifiers` lesson.

## What to decide

1. **What does `Schema` mean on MySQL?** Honouring it silently crosses *databases*, which is a different
   blast radius from a schema. Refusing (throw at table load, per § SH-H037) is probably right, but it is a
   decision to record, not to infer. "Same word, two meanings" is the trap.
2. **Is there also a default/search-path setting?** Complementary and orthogonal to per-entity qualification —
   and cheaper. It may even be the whole feature a consumer actually wants; decide before building the bigger
   one.
3. **Every sink, enumerated before any are changed.** A qualified name has to be honoured in `CreateTable`,
   `DropTable`, `SelectTableReference` (which emits `FROM "T" AS T` — note the **alias** must stay bare and
   unqualified, per TASK-211), `AddRequiredWhere`'s qualifier stripping (TASK-216), index DDL, view DDL,
   `AlterTableAdd`/`Drop`, the migrations `SqlSchemaBuilder`, and `IsHypertable`'s catalogue probe. This
   family's record is that a funnel with one missed sink is worse than no funnel.

## Acceptance criteria

- [ ] A decision recorded on each of the three questions above, with reasons.
- [ ] `Schema` declarable per entity and honoured end to end on **PostgreSQL and MSSql**: create, read, write,
      index, and drop an entity in a non-default schema, asserted against the catalogue and against returned
      rows — not against the absence of an exception (TASK-209).
- [ ] The `false` side of `SupportsSchemas` asserted on MySQL and SQLite, so the capability is not
      indistinguishable from unconditional.
- [ ] The bare-alias invariant preserved: `SelectTableReference` may qualify the *relation* but the alias must
      stay bare, or every qualifier stops resolving on PostgreSQL (TASK-211).
- [ ] Every sink from question 3 either honours the schema or is recorded as deliberately not doing so.
- [ ] Proven able to fail.

## Out of scope

- The caller-supplied qualified *string* in the TimescaleDB emitters — **[[TASK-262]] did that** and it is
  independent of this feature.
- Routing those emitters through the connector — [[TASK-271]].
- The connector-caching pattern — [[TASK-270]].

## Human test plan

- [ ] N/A — mechanical; the proof is an entity's rows and its DDL landing in the declared schema, readable from
      the catalogue.
