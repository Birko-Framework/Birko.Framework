---
id: TASK-031
parent: STORY-022
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Models.* validation tests

## Context

Lightweight tests on the `Birko.Models.*` projects — constructors, value-object invariants, contract implementations. Covers Inventory / Pricing / Users / Customers / Accounting / Product / Category.

## Acceptance criteria

- [ ] `Birko.Models.Tests` (or per-domain test projects) exist for the model siblings
- [ ] Value-object invariants tested (Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
- [ ] Contract implementations verified (ICatalogItem, IPriceable, IHierarchical etc.)
- [ ] Equality / immutability / serialization round-trip where applicable
- [ ] Wired into `Birko.Framework.slnx`

## Out of scope

- SQL mapping behaviour (covered by `Birko.Models.SQL.Tests` and platform tests)
