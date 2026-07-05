---
id: STORY-035
parent: EPIC-015
status: in-progress
created: 2026-06-25
---

## Progress (2026-07-05)

- **tree-menu** — `TreeView` + `TreeViewItem` token restyle (`Controls/Tree.axaml`): expander
  chevron, token hover/selected, indented children, `:empty` hides the chevron on leaves. Verified
  (nested render/expand tests + screenshot). Chevron rotation keys off the expand toggle's `:checked`
  (real click-driven use); the programmatic headless capture shows the resting chevron.

- **command-palette** — `CommandPalette` control (`Controls/CommandPalette.cs` + Blocks.axaml
  template) over a platform-neutral `CommandItem` model (Core): search filter, keyboard nav
  (Up/Down/Enter/Esc), invoke `Run` + close. Verified (filter/invoke/close tests + screenshot). Also
  the **STORY-036** shell palette — the control exists; wiring Ctrl+K into `ShellView` is a small follow-up.

- **object-tree / JSON viewer** — `ObjectTree` (`Controls/ObjectTree.cs`): `Source` (object graph) or
  `Json` (string) → recursive tree over the restyled `TreeView`, type-colored monospaced values
  (string/number/bool/null), walks `JsonNode`/dict/enumerable/POCO; invalid JSON → raw-string leaf.
  Verified (json/object/invalid tests + screenshot). Covers `b-object-tree` + `b-json-viewer`.

**Remaining composites:** xml-viewer, kanban, markdown-editor, chart (chart needs a both-platforms
plotting lib — LiveCharts2/ScottPlot/OxyPlot per the EPIC).

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
