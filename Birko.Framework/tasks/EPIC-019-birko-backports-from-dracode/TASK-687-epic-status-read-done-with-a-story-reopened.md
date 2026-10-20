---
id: TASK-687
parent: EPIC-019
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# EPIC-019 read done while STORY-058 was reopened

## Context

Found by `/adopt-project` (2026-10-20). TASK-678 (public single refresh-token revoke) was filed under STORY-058 on
2026-10-19 and the story went back to `in-progress`, but EPIC-019 kept `status: done`. A reader of the epic, or a
dashboard rolled up from it, would take the DraCode backports as finished with a todo task under them.

## Acceptance criteria

- [x] EPIC-019 reads `in-progress` while STORY-058 has open work.

## Out of scope

- TASK-678 itself.

## Resolution

EPIC-019's `status:` set from `done` to `in-progress`. Verified: STORY-058 reads `in-progress` and TASK-678 `todo`;
no other story under the epic is open.
