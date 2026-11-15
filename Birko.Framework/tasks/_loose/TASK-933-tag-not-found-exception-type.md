---
id: TASK-933
parent: null
feature: null
status: done
priority: P2
assignee: agent
created: 2026-11-15
depends-on: [TASK-147]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Tagging: a missing tag raises its own `TagNotFoundException`

## Context

Requested by the Symbio session for Symbio TASK-1092, which maps both of TASK-147's attach refusals to 404.
TASK-147 raised a plain `InvalidOperationException` ("Tag {id} not found.") for a missing tag. To catch that,
Symbio would have to catch every `InvalidOperationException`, which would also turn unrelated failures inside
attach into 404s. `CrossTenantTagAccessException` was already its own type.

## Change

- New `TagNotFoundException : InvalidOperationException` (`Birko.Data.Tagging/Services/TagNotFoundException.cs`)
  carrying `TagId`, with the same message "Tag {id} not found.". It derives from `InvalidOperationException`, so
  existing catches still work.
- `TagService.TagNotFound` (one producer) now returns it, so `AttachTagAsync`, `SetEntityTagsAsync` and
  `UpdateTagAsync` all raise it.

## Acceptance criteria

- [x] All three paths raise exactly `TagNotFoundException` with the right `TagId` and the unchanged message.
- [x] It is still an `InvalidOperationException`.

## Human test plan

N/A — fully covered by automated tests. The three missing-tag tests were tightened to `ThrowExactlyAsync<TagNotFoundException>`
plus `TagId`. They failed against the old throw (3 of 28) and pass now (28/28).
