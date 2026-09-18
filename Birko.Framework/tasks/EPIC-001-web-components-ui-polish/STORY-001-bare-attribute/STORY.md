---
id: STORY-001
parent: EPIC-001
status: in-progress
created: 2026-05-28
---

# bare attribute for inline form usage

## User story

As a developer building dense UI (toolbars, table cells, floating action bars), I want a `bare` attribute on Birko form components so I get the input element without the standard label / error chrome.

## Behaviour

- `bare="true"` skips rendering `.field` + `renderLabel` + error `<span>`
- Control element emitted directly under the shadow root
- Error / label state still accessible via attributes — caller is responsible for positioning
- Pairs naturally with `size="sm"` for dense layouts
