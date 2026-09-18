---
id: STORY-022
parent: EPIC-011
status: planned
created: 2026-05-28
---

# Phase 4 lower-priority tests

## User story

As a maintainer, I want lightweight tests on Models / ViewModel CRUD / Configuration / Contracts so refactors don't silently break consumers.

## Behaviour

- Validation tests on the `Birko.Models.*` projects (constructors, value-object invariants)
- CRUD-pattern tests on `Birko.Data.*.ViewModel` repositories
- DTO-shape tests on `Birko.Configuration` and `Birko.Contracts`
- Each test project follows the standard xUnit + FluentAssertions convention
