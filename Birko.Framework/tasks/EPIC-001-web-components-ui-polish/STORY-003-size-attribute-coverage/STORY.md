---
id: STORY-003
parent: EPIC-001
status: planned
created: 2026-05-28
---

# size attribute coverage

## User story

As a developer, I want `size` variants on the components that still lack them so they fit in dense layouts without manual CSS overrides.

## Behaviour

- `b-pagination` accepts `size="sm"` / `size="lg"` (chip category per `CLAUDE.md`)
- `b-dropdown-menu` accepts `size="sm"` (text-scale category)
- `b-breadcrumb` adds `size` only if a concrete use case appears; document the decision either way
- Existing default size unchanged on all three
