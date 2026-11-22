---
id: TASK-972
parent: EPIC-001
feature: FEATURE-024
status: in-progress
priority: P2
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# App shell: a snippet that applies the saved theme before the first paint

## Context

FEATURE-024 D5: before the app's code loads, the page shows the light look, so a dark browser on Auto (or a saved dark) flashes light first. The framework documents a short script an app puts in its page head that applies the saved theme, Auto included, before the first paint. Built on `task/TASK-968` for v0.5.2.

## Acceptance criteria

- [ ] The Shell README gives the snippet, with the app's storage prefix and starting theme to fill in.
- [ ] The snippet applies what the shell would apply (light, a saved theme, Auto resolved) and fails silently where storage is blocked.
- [ ] Playground check of the snippet's logic.

## Human test plan

1. In an app with the snippet and Auto, with the system set to dark: reload. The page is dark from the first frame, with no light flash.
