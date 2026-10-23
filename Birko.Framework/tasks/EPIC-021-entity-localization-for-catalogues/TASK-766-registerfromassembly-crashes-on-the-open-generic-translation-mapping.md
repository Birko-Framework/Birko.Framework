---
id: TASK-766
parent: EPIC-021
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] `RegisterFromAssembly` skips types with `ContainsGenericParameters` (open generics cannot be instantiated; the
      consumer registers them closed, through `LocalizationSqlMappings.Register<TTranslation>(registry, table)`).
- [ ] A test scans an assembly holding an open generic `IModelMapping<>` beside a closed one: no throw, the closed one is
      registered, the open one is not. Mutation-proven: removing the skip makes it throw.
- [ ] The XML-doc of `RegisterFromAssembly` says open generics are skipped and how to register them.

## Out of scope

- SH-L354 (a mapping without a public parameterless constructor aborts the whole scan), unless the same guard fixes it
  for free; say so here if it does.
- The `UpdatedAt` type clash with `TenantEntity`-style models: TASK-767.

## Human test plan

N/A — covered by the test; the consumer-side proof is Symbio TASK-945's host boot.

## Implementation plan

_Small; drafted when picked._
