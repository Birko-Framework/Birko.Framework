---
id: TASK-766
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `RegisterFromAssembly` crashes on the open generic `EntityTranslationMapping<TTranslation>`

## Context

Found 2026-10-23 while planning Symbio TASK-945, the first consumer of EPIC-021. The Symbio side is blocked on this.

`ModelMapRegistry.RegisterFromAssembly` (`Birko.Models.SQL/Mapping/ModelMapRegistry.cs:22-41`) finds every non-abstract,
non-interface type implementing `IModelMapping<>` and calls `Activator.CreateInstance` on it.
`Birko.Data.Localization.SQL/Mappings/EntityTranslationMapping.cs:32` declares
`public sealed class EntityTranslationMapping<TTranslation> : IModelMapping<TTranslation>`. That is an **open generic**,
and the only one in the framework. `Activator.CreateInstance` on it throws.

A consumer that compiles shared projects into one assembly is hit as soon as it imports `Birko.Data.Localization.SQL`.
Symbio's `Symbio.Birko` is such an assembly. Its host calls `RegisterFromAssembly` on it at boot (`Program.cs:73`), and four
Symbio guards scan the same assembly. No framework test scans an assembly that contains the open mapping, so the suite is
green.

## Acceptance criteria

- [x] `RegisterFromAssembly` skips types with `ContainsGenericParameters` (open generics cannot be instantiated; the
      consumer registers them closed, through `LocalizationSqlMappings.Register<TTranslation>(registry, table)`).
- [x] A test scans an assembly holding an open generic `IModelMapping<>` beside a closed one: no throw, the closed one is
      registered, the open one is not. Mutation-proven: removing the skip makes it throw.
- [x] The XML-doc of `RegisterFromAssembly` says open generics are skipped and how to register them.

## Out of scope

- SH-L354 (a mapping without a public parameterless constructor aborts the whole scan), unless the same guard fixes it
  for free; say so here if it does.
- The `UpdatedAt` type clash with `TenantEntity`-style models: TASK-767.

## Human test plan

N/A — covered by the test; the consumer-side proof is Symbio TASK-945's host boot.

## Outcome (2026-10-24)

All criteria met.

- **Fix.** `ModelMapRegistry.RegisterFromAssembly` filters out types with `ContainsGenericParameters` before it
  instantiates anything. Its XML doc says open generics are skipped and names the two ways to register one closed:
  `Register(new X<TModel>())` and, for the translation table, `LocalizationSqlMappings.Register<TTranslation>(registry,
  table)`. Birko.Models.SQL's README and CLAUDE.md say the same in one line.
- **Test.** `ModelMapRegistryApplyTests` gains a private open generic `OpenMap<T> : IModelMapping<T>` next to the
  existing closed `WidgetMap`, so the test assembly itself is the "assembly holding an open generic beside a closed one".
  - `RegisterFromAssembly_skips_an_open_generic_mapping_and_registers_the_closed_one`: no throw, `Widget` is
    registered, neither the generic parameter `T` nor `Gadget` is.
  - `An_open_generic_mapping_registers_once_closed`: after the scan, `Register(new OpenMap<Gadget>())` maps `Gadget`.
- **Mutation** (skip removed, then restored): **3 of 44 red**, all with `ArgumentException: Cannot create an instance
  of …OpenMap`1[T] because Type.ContainsGenericParameters is true`.
  - Depends on the fix: `RegisterFromAssembly_skips_an_open_generic_mapping_and_registers_the_closed_one`, the test
    written for it.
  - Red as collateral, because `OpenMap<T>` now lives in the assembly they scan:
    `RegisterFromAssembly_DiscoversMappings_AndGetMapReturnsConfigured` (pre-existing, CR-L322) and
    `An_open_generic_mapping_registers_once_closed`. The second is a contract pin for closed registration; it goes red
    only through its own scan. That is deliberate: every future scan test in this assembly now meets an open mapping,
    the way a consumer assembly does.
  - Birko.Models.SQL.Tests 44/44 restored.
- **Regression:** every test project importing `Birko.Models.SQL.projitems` is green (29 suites, 2 293 passed, 15 skipped; offline:
  no `BIRKO_*_HOST` was set, so live-gated tests skipped). `dotnet build Birko.Framework.slnx` succeeds with 0 errors.
  Live runs add nothing here: the change is pure reflection and runs before any connection opens.
- **SH-L354 is not fixed by this guard, and is left alone.** SH-L354 is about `ViewMapRegistry.RegisterFromAssembly`
  (Birko.Data.Views), a different registry, and its cause is a mapping with no public parameterless constructor
  (`MissingMethodException`), which `ContainsGenericParameters` does not detect. `ModelMapRegistry` has the same
  missing-constructor gap; it is not filed separately here.
- **Flagged:**
  - `ViewMapRegistry.RegisterFromAssembly` has the same open-generic hazard (no `ContainsGenericParameters` check).
    No open generic `IViewMapping<>` exists in the framework today, so nothing crashes; it is not fixed here.
  - `docs/specs/schema-index-and-ddl.md` still says `RegisterFromAssembly` SHALL instantiate "every non-abstract,
    non-interface type implementing `IModelMapping<>`". Specs are regenerated, not hand-edited, so the next regen of
    that area should pick up the skip. This spec diff is expected.
  - Symbio verifies from its side (its TASK-945 step 0); the Symbio repo was not touched.
- **Recent Updates:** an entry was added, which put the entries over 10 KB (10 409 bytes). The oldest entry
  (TASK-686, 2026-10-20) was rolled into CHANGELOG.md, leaving 9 773 bytes.

## Implementation plan

_Small; drafted when picked._
