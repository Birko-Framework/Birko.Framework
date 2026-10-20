---
id: TASK-692
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-691]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Fluent `HasPrecision` / `HasScale` are accepted and dropped

## Context

Found while planning [[TASK-684]] / [[TASK-691]] (2026-10-20). `ModelMapRegistry.ApplyToDatabase` documents that
`HasPrecision` and `HasScale` are not applied. A fluent decimal mapping therefore silently gets the provider's default
precision, while `[PrecisionField]` / `[ScaleField]` would be honoured. That is rule 55: a builder method that accepts a
declaration and does nothing.

About 50 string columns in Users, Customers, Product, Pricing and Inventory call `HasPrecision(n)`, apparently meaning a
length. On the attribute path `[PrecisionField(n)]` on a string *is* a length (`CharField`), so honouring it on the
fluent path would change those columns. Measure what those mappings render today before deciding.

TASK-691 adds the registration channel (`DataBase.RegisterMaxLength`) that this fix would extend.

## Acceptance criteria

- [x] Decide and record what fluent `HasPrecision` / `HasScale` mean on decimals and on strings: honoured,
      refused, or a string's `HasPrecision` mapped to a length. The ~50 existing string usages are measured first.
- [x] Whatever is accepted is honoured by every SQL provider's DDL; what is not is refused (rule 55).
- [x] Tests in Birko.Models.SQL.Tests, plus a catalogue read-back on one live provider (rule 63).

## Out of scope

- `HasMaxLength` itself: [[TASK-691]].

## Human test plan

N/A — library contract, asserted by tests.

## Implementation plan

_Planned 2026-10-20 after measuring. Decided by the user: refuse `HasPrecision` / `HasScale` on strings, convert the
framework's string sites to `HasMaxLength`, and migrate WorkoutTracker. FisData.Stock is skipped: Symbio supersedes it._

**Measured (2026-10-20).** Every `HasPrecision` / `HasScale` in the framework's mappings was rendered with the MySQL and
SQL Server connectors' `ConvertType`:

| Calls | What renders today |
|---|---|
| 13 decimal `HasPrecision(22).HasScale(6)` | `DECIMAL(22,6)`, only because that is the framework default (TASK-512) |
| 66 string `HasPrecision(n)`, not a key | `LONGTEXT` / `NVARCHAR(MAX)`: the declared length is ignored |
| 7 string `HasPrecision(n)`, unique or indexed | `VARCHAR(255)` / `NVARCHAR(255)` from rule 37, not the declared 8 / 50 / 100 / 256 |

Consumers: WorkoutTracker has about 20 string uses, and decimal `WeightKg (6,2)` / `WaistCm (5,1)`, which are silently
`(22,6)` today. FisData.Stock.Core has about 30 (skipped). Symbio has none.

1. `DataBase.RegisterPrecision(type, property, precision, scale)`, beside `RegisterMaxLength`. `LoadField` appends
   `PrecisionField` / `ScaleField` after the property's own attributes, so `CreateAbstractField` stays the one producer
   and the fluent value wins. A changed value drops the type's caches; the same value again changes nothing.
2. `ModelMapRegistry.ApplyToDatabase` registers them before any table loads. It refuses (rule 55):
   - a type other than `decimal` / `decimal?` (strings say "use HasMaxLength");
   - a precision that is not positive;
   - a negative scale, or a scale above the precision;
   - a property that is no column.
3. The framework's 73 string `HasPrecision(n)` become `HasMaxLength(n)`. Tests that read `Precision` read `MaxLength`.
4. Tests: Birko.Models.SQL.Tests (decimal honoured, fluent over attribute, scale alone, the refusals, derived types).
   Column types on MySQL and SQL Server. A live catalogue read-back of `NUMERIC_PRECISION` / `NUMERIC_SCALE` on one
   provider (rule 63). Mutations. Regression: every importer of Birko.Models.SQL.
5. WorkoutTracker: string `HasPrecision` becomes `HasMaxLength`. Build it and run its tests.

## Outcome

Done 2026-10-20.

**Production change:**
- New `DataBase.RegisterPrecision`. `LoadField` appends `PrecisionField` / `ScaleField` after the property's own
  attributes, through one lookup it shares with the length registration (rule 16).
- `ApplyToDatabase` registers before any table loads. It refuses a non-decimal (a string names `HasMaxLength`), a
  non-positive precision, a negative scale or one above the precision, and a property that is no column.
- 74 framework string `HasPrecision(n)` calls are now `HasMaxLength(n)`.
- Re-measured after: every converted column renders its declared length on MySQL and SQL Server (`Currency.Code`
  `VARCHAR(8)`, `User.Email` `VARCHAR(256)`, …). Decimals are unchanged at `(22,6)`.

**Test fixtures:** the 50 test fixtures that called a string `HasPrecision(100)` drop the call rather than converting
it, as TASK-257 did. They always ran on unbounded strings, and converting would change what they measure.
`Precision.Should().NotBe(0)` assertions on strings would now pass vacuously, so they read `MaxLength` (Customers,
Pricing, Product, Inventory, Users).

**Tests:**
- `FluentPrecisionTests` (13): decimal and nullable decimal; fluent over attribute; scale alone; the MySQL and
  SQL Server column types; the refusals; derived types; the same value twice keeps the cached field.
- `FluentPrecisionLiveTests` (MySQL live): `information_schema` reports `decimal(6,2)`, `decimal(5,1)` and
  `varchar(2000)` (rule 63).

**Mutations, all red (11):**
- precision or scale never appended (4 red each);
- the attribute wins;
- the same value re-registers;
- no base-type walk;
- string, other type, scale above precision, non-positive precision, or non-column accepted;
- the registration pass removed (12 red).

**Regression:** all 29 test projects importing Birko.Models.SQL are green, live where gated. The first run caught the
refusal firing on the 50 test fixtures, which is what it is for.

**Consumers:**
- WorkoutTracker: 20 string calls became `HasMaxLength`. `WeightKg (6,2)` and `WaistCm (5,1)` now take effect. Its
  solution builds, and Reps.Domain.Tests 267/267 and Reps.Api.Tests 230/230 pass. The edits are left uncommitted in
  that repo, which has other work in progress.
- FisData.Stock is skipped: Symbio supersedes it (decided by the user).

**Breaking:** an existing table on MySQL, SQL Server or PostgreSQL keeps its unbounded columns and reports drift until
altered.
