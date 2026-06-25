---
id: STORY-035
parent: EPIC-015
status: planned
created: 2026-06-25
---

# Tier 2 — composite controls with no native peer

## User story

As an app developer, I want the rich composite controls that have no native equivalent, so
desktop apps get the same advanced surfaces (ribbon, command palette, kanban, viewers) as web.

## Behaviour

- Custom `TemplatedControl`s for: `Ribbon`, `CommandPalette` (Ctrl+K), `Kanban` (columns + DnD + nesting), `TreeMenu`, the data viewers (`JsonViewer` / `XmlViewer` / `ObjectTree`), `MarkdownEditor`, and `Chart`.
- **`Chart`** depends on a plotting library — and per WPF addendum constraint #2 the choice **must target both Avalonia and WPF** (LiveCharts2 / ScottPlot / OxyPlot) so a future WPF skin doesn't force a rewrite. Pick the lib in this story.
- Each control consumes tokens only and matches its `b-*` counterpart visually + behaviorally (keyboard nav, DnD zones, expand/collapse) across themes.
- Control *behavior* code should be written so the platform-neutral parts could be shared with a WPF skin where practical (constraint #3), though `StyledProperty` vs `DependencyProperty` means code-behind largely forks.
- Highest-effort tier — sequence after Tier-1 (STORY-034) is proven.
