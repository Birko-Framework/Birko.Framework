---
id: TASK-273
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `CompositeIndex` cannot express a filter predicate, so a unique index over a NULLABLE column is unusable on MSSql

## Context — raised by consumer Symbio while ruling its v1 frozen schema (Symbio TASK-463 / TASK-481)

`CompositeIndex` carries exactly three things (`Birko.Data.SQL/Attributes/Field.cs:131-142`):

```csharp
public class CompositeIndex : System.Attribute
{
    public string   Name       { get; }
    public string[] Properties { get; }
    public bool     IsUnique   { get; set; }
}
```

There is no way to say *"unique **where** the column is not null"*. `IndexDefinition`
(`SQL/Tables/IndexDefinition.cs:5`) carries `Name` / `Unique` / `Columns` and nothing more, and
`AbstractConnectorBase.CreateIndexSql` (`SQL/Connectors/AbstractConnectorBase.cs:740`) emits
`CREATE {unique}INDEX {name} ON {table} ({columns})` with no tail.

**Why a consumer needs it.** Symbio's `CustomerAccount` declares per-tenant uniqueness on `Email` as
`[CompositeIndex("ux_customeraccount_email", TenantGuid, Email, IsUnique = true)]`, while the sibling
`ExternalId` — `public string? ExternalId` — carries **no index at all** and is enforced only by a
service-side read-then-write. That is a race, and Symbio TASK-481 had to accept it *because this attribute
cannot express the index that would close it*. Storage-level uniqueness is exactly the backstop this
framework provides everywhere else.

**Why the obvious index is wrong rather than merely missing.** `ExternalId` is nullable and most rows have
no external id. SQL Server treats NULLs as **equal** for uniqueness purposes, so a plain
`UNIQUE (TenantGuid, ExternalId)` admits **one** NULL row per tenant and rejects the second ordinary
account. PostgreSQL, SQLite and MySQL treat NULLs as **distinct** and admit any number. So adding the index
without a predicate does not merely under-enforce — on MSSql it **breaks ordinary inserts**, and only there.

⚠ **Verify that per-provider NULL behaviour on the live servers before building anything.** It is stated
above from standard SQL semantics, not measured, and this repository's rule is that a provider claim is
worth what its live test says. All four suites gate on `BIRKO_*_HOST` and skip silently, so set
`BIRKO_REQUIRE_LIVE`.

⚠ **TASK-257 raised the stakes rather than lowering them.** Before it, an unlengthed `string` declared
`TEXT` on MSSql and `TEXT` is not a legal index key (Msg 1919) — so a unique index over such a column could
not be built there **at all**, and this gap was moot. Since TASK-257 an indexed column declares
`NVARCHAR(255)` and those indexes build, which is what turns "MSSQL treats NULLs as equal" into a live
concern for every nullable indexed column a consumer declares from now on.

⚠ **The portability objection is smaller than it looks — check it before scoping the feature wide.**
MySQL is the one provider of the four with **no partial/filtered index support**, which reads at first like
a blocker. But MySQL is also one of the three that already treat NULLs as distinct, so for *this* use it
does not need the predicate: an unfiltered unique index there already admits many NULLs. The predicate is
needed only where filtered indexes exist (MSSql, PostgreSQL, SQLite). A **general-purpose** predicate
(`WHERE IsActive = 1`) is a different, wider feature and is genuinely unimplementable on MySQL — decide
which of the two is being built and say so, rather than discovering it at the fourth provider.

## Acceptance criteria

- [ ] Per-provider NULL-in-unique-index behaviour **measured live** on all four (MSSql 2022, PostgreSQL 16,
      MySQL 8.4, SQLite), with `BIRKO_REQUIRE_LIVE` set so a missing server fails instead of skipping. The
      claim above is the hypothesis, not the result.
- [ ] A decision, recorded on this task: **(a)** a narrow "exclude NULLs" flag, or **(b)** a general
      predicate string. (a) is portable across the three providers that need it and is a no-op on MySQL;
      (b) cannot be honoured on MySQL and needs an explicit policy for it.
- [ ] The chosen shape threaded through all four sites: the attribute
      (`Attributes/Field.cs`), `IndexDefinition`, whatever populates it from the attribute
      (`DataBase_Table.LoadIndexes`), and `CreateIndexSql` — plus any provider override that reimplements
      the statement.
- [ ] ⚠ **A provider that cannot honour the predicate must not silently emit the index without it.** That
      converts a declared constraint into a *different, stricter* constraint that rejects legitimate rows —
      worse than not creating it. Whatever the MySQL policy is, it is loud: either refuse at schema-ensure
      or record the failure the way TASK-354's `IndexCreationFailures` does.
- [ ] Live behavioural tests per provider, asserting **both** directions: two NULL rows are accepted, and
      two rows sharing a non-NULL value are rejected. A one-directional test passes against an index that
      was never created.
- [ ] Mutation-proven: drop the predicate from the emitted SQL and the MSSql test must go red.
- [ ] Existing declarations unaffected — the six entities carrying `CompositeIndex` today declare no
      predicate and their emitted DDL must be byte-identical.

## Out of scope

- Symbio's own decision about whether `CustomerAccount.ExternalId` should be unique at storage — that is
  Symbio TASK-481. This task supplies the capability; the consumer decides whether to use it.
- Foreign-key support. Also absent from this framework (`FOREIGN KEY` and `ON DELETE` occur nowhere in
  `Birko.Data.SQL` outside comments, and 0 of 146 tables in a real consumer database carry one) and also
  raised by the same Symbio freeze pass, but it is a much larger feature and is tracked separately by the
  consumer as Symbio TASK-540 pending its decision.
- Retrofitting predicates onto any existing declaration.

## Human test plan

- [ ] N/A — the verification is four live-server behavioural suites plus a mutation. Nothing here is
      exercised by a person driving a product.

## Implementation plan

_Populated by `/tasks plan TASK-273` — leave empty until then._
