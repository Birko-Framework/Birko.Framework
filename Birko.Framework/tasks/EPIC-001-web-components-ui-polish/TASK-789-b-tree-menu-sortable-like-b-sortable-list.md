---
id: TASK-789
parent: EPIC-001
status: verify
priority: P2
assignee: ai
created: 2026-10-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-tree-menu's sortable mode should reorder by drag and keyboard, in line with b-sortable-list

## Context

Asked by the user through the Symbio session on 2026-10-30. Symbio found it while moving its hand-made ▲▼ lists to
`b-sortable-list` ([[TASK-787]], Symbio TASK-1006): the Presentation page tree is the one list left whose arrows are the
framework's, so Symbio cannot fix it alone. Symbio's side is Symbio TASK-1012, blocked on this task.

**Today** (`Birko.Web.Components/src/nav/b-tree-menu.ts`, controls ~373–378, wiring ~467–487, drag ~491–558), with
`sortable` on, each node shows:
- a ▲ button, an editable order number (`.sort-order`) and a ▼ button (`.sort-btn`), all `tabindex="-1"`, so the
  keyboard cannot reach them. The titles "Move up" / "Move down" are hard-coded English;
- native HTML5 `draggable` on the row: drop zones before / inside / after (top 25%, middle, bottom 25%), emitting
  `reorder` `{ sourceId, targetId, position, sourceItem, targetItem }`. HTML5 drag does nothing on touch;
- the buttons emit `sort` `{ id, direction, item }`, the number input emits `sort-order` `{ id, sortOrder, item }`.

Nothing is announced, and there is no drop indicator in `b-sortable-list`'s style.

## Ask (from Symbio, to be decided with the user)

- A handle per node; drag by pointer (mouse, pen, touch hold) as in `b-sortable-list`, with its drop bar.
- Keyboard: Space/Enter picks a node up, arrows move it, Space drops, Escape puts it back; each step announced.
- Possibly moving a node into another parent (`reorder` already carries `position: 'inside'`).
- The same look as `b-sortable-list`.
- **Keep `reorder` compatible** — Symbio consumes it.

## Open decisions (preview first — a stakeholder-facing surface, as for TASK-787 / 788)

1. Do the ▲ / number / ▼ controls go, or stay behind an option? What happens to the `sort` and `sort-order` events
   (keep emitting, deprecate, remove — Symbio is the known consumer)?
   **Consumer usage (Symbio session, 2026-10-30):** four sortable trees, all `sortable: canUpdate`.
   `presentation/categories/categories-page.ts:185-197`, `presentation/navigation/navigation-page.ts:202-214` and
   `products/categories/categories-page.ts:183-195` listen to `sort`, `sort-order` **and** `reorder`;
   `presentation/pages/pages-page.ts:314` to `reorder` only (`building/structure-page.ts` is not sortable). So
   removing `sort` / `sort-order` breaks three Symbio pages; deprecating them (still emitted, e.g. alongside `reorder`)
   lets Symbio move all four trees to `reorder` in Symbio TASK-1012. Symbio's specs do not click `.sort-btn`.
2. Reparenting: allowed by keyboard too (e.g. →/← to indent / outdent) or only by drag? Any veto hook (a cancelable
   `reorder`, or a `canDrop(source, target, position)` callback) for trees where some nodes may not hold children?
3. Whole row drags or handle only (tree rows are also click-to-select and expand toggles — likely handle-only, or a
   hold)?
4. Collapsed parents: does hovering a collapsed parent during a drag expand it after a delay?
5. Share code with `b-sortable-list` (a common drag/keyboard helper) or reimplement within the tree?

## Steps

1. Preview options for decisions 1–4 for the user (Playground or a mockup); record the answers here.
2. Implement; keep `reorder`'s detail shape; localize every new label (`bwc.treeMenu.*`).
3. Playground smoke suite (keyboard moves, announcements, `reorder` detail, reparent, compat), plus a real-input
   drag check like `sortable-drag-check.mjs`; mutation-prove the key assertions.
4. API.md, ACCESSIBILITY.md, CHANGELOG, version bump; tell the Symbio session for TASK-1012.

## Decisions (2026-10-30, the user, on a clickable preview)

Preview: https://claude.ai/artifact/2HBM1ZaboG4F1TrpKb6XgW

1. **The ▲ / number / ▼ controls stay behind an option.** A sortable tree shows a grip per node by default; the old
   controls render only with the option on. `sort` and `sort-order` keep firing from them, documented as deprecated.
2. **Reparenting by keyboard too**: while a node is picked up, → moves it into the node above it (as the last child), ←
   moves it out to after its parent. **No veto hook**: the user did not take `canDrop`. A node still cannot be dropped
   into itself or its own descendant, which today's drag allows.
3. **As in b-sortable-list**: the whole row drags (a touch needs a hold, so a swipe still scrolls), and a `handle-only`
   option limits it to the grip.
4. **A collapsed parent stays closed** under a drag. Dropping inside it still works.
5. **One shared helper** for the pointer drag, the touch hold, edge scrolling and the click swallowed after a drag,
   used by b-sortable-list and b-tree-menu.

## Implementation plan

1. **Shared helper** (`Birko.Web.Components/src/pointer-drag.ts`, beside `dom-utils.ts`): b-sortable-list's pointer
   machinery moved out as is — arming on a handle or a row (a press on an interactive element is not a drag), the
   350 ms touch hold, the 4 px start and 8 px swipe cancel, pointer capture, `pressing` / `dragging` classes, the
   follow transform, the edge auto-scroll with its scroller lookup, Escape and `pointercancel`, the swallowed click,
   and the context-menu / touchmove guards. The host supplies `target(x, y)` (where it would land, drawing its own
   indicator) and `drop(cancelled)`. b-sortable-list is rewired onto it with no behaviour change: its smoke suites and
   `sortable-drag-check.mjs` are the regression gate, run before anything is built on the helper.
2. **Tree drag** (b-tree-menu, `sortable: true`): the HTML5 `draggable` path goes. Each node gets a grip button
   (b-sortable-list's grip icon, `aria-roledescription`, label "{label}, position {p} of {n}"). The whole row drags
   unless `handleOnly: true` in the config; a click with no drag still selects. Hit-testing keeps today's zones (top
   25 % before, bottom 25 % after, middle inside); before / after draw b-sortable-list's drop bar, inside the
   existing outline. A spot inside the dragged node's own subtree is refused (no indicator, announced once).
3. **Keyboard** on the grip: Space / Enter picks up, ↑ ↓ move among siblings, → into the node above (expanding it,
   loading it through `onExpand` if needed, so focus can follow), ← out to after the parent, Space / Enter drops,
   Escape restores the start. Each step is announced with its level ("position 2 of 3 in About us"); the grip keeps
   focus across the re-renders.
4. **Data**: the tree moves nodes in its own copies of the child arrays, never in the consumer's arrays or items, and
   emits one `reorder` per completed move with today's detail `{ sourceId, targetId, position, sourceItem,
   targetItem }`. A keyboard move describes its final spot against a neighbour: after the previous sibling, else
   before the next, else inside the parent. A consumer that re-sets the items on `reorder` (Symbio's four pages)
   keeps working.
5. **Old controls**: `sortControls: true` renders ▲ / number / ▼ as today, with their titles localized; `sort` /
   `sort-order` unchanged. **This changes what a `sortable` tree shows by default** (the arrows go), so the release is
   **v0.4.0** with a BREAKING note: a page that relies on the arrows sets `sortControls: true` or moves to `reorder`.
6. **Labels**: every new string under `bwc.treeMenu.*` (grip, hint, picked up, position, first / last, into, out of,
   no node above, top level, dropped, cancelled, moved, refused), English in the source with fallbacks; Slovak in the
   Playground's sk.json for the human test.
7. **Proof**: a Playground smoke suite for the tree (keyboard pick-up / move / reparent / drop / Escape, each
   announcement, the `reorder` details, refusal of a descendant, `handleOnly`, `sortControls` still emitting `sort`
   and `sort-order`, no listener growth across re-renders), and `sortable-drag-check.mjs` extended to the tree with
   real mouse and touch input (hold, swipe scrolls). Mutation-prove the key assertions. Then verify.mjs,
   rerender-leak, entry-points, locale-keys, a11y-name, keyboard-check, Components tsc.
8. **Docs and release**: API.md (b-tree-menu sortable, the helper is internal), ACCESSIBILITY.md, Components CLAUDE.md
   entry, CHANGELOG v0.4.0, versions; the Playground Tree menu card gains a sortable demo with its options. Tell the
   Symbio session for TASK-1012.

## Human test plan

1. Playground, Tree menu card (sortable): drag a node before, after and into another with the mouse, and drag a row
   (not its grip). Dropping a node into its own child does nothing. With `handle-only` on, only the grip drags.
2. Same card by keyboard: Tab to a grip, Space, ↑ ↓, → into the node above, ← back out, Space; then pick one up and
   Escape. A screen reader (NVDA or Narrator) reads each step.
3. Touch (emulation now, a real device when one is to hand): hold a row and drag; a quick swipe scrolls instead.
4. `sortControls` on: ▲ / ▼ and the number still work and show their titles in Slovak after a switch.
5. Symbio (TASK-1012): Presentation → Pages, reorder and reparent a page by drag and by keyboard; the order is saved.

## Resolution (2026-10-30, on branches, not merged)

Worktrees: `C:\Source\Birko\Web.TASK-789` and `C:\Source\Birko\Consumers\Birko.Web.Playground.TASK-789`, branch
`task/TASK-789` in both. `C:\Source\Birko\Web` stays on main: Symbio's dev server watches it (a gate run built a
half-done step 1 for about 30 s before the work moved to the worktree; Symbio restarted it).

- Birko.Web `b27f527` (step 1): `pointer-drag.ts` `PointerDrag`; b-sortable-list rewired with no behaviour change
  (sortable-drag-check 13/13, sortable-smoke 60/60, verify.mjs 0 failing).
- Birko.Web `ee9861d` (**v0.4.0**): b-tree-menu as decided; en.json `bwc.treeMenu.*` (20 keys); API.md,
  ACCESSIBILITY.md, Components CLAUDE.md, CHANGELOG (BREAKING: the arrows need `sortControls: true`), versions.
- Playground `e5f95d5`, `c126371`: `tree-sort-smoke` 27/27; `tree-drag-check.mjs` 17/17 (a separate file rather than an
  extension of sortable-drag-check, as plan step 7 said); a "Tree menu, sortable" card with handle-only and
  sort-controls selectors; sk.json `bwc.treeMenu.*`.
- Mutations, each caught: no copy before a move (the consumer's array reorders), no subtree exclusion and no subtree
  guard (a page error), Escape propagating, no focusout drop, a wrong keyboard neighbour, handleOnly ignored, the grip
  click selecting, no touch hold, the arrows shown by default. A row-click guard for the grip was dead code (the grip's
  own handler stops the click) and was removed.
- Gate on the branch: verify.mjs 0 failing, keyboard-check 222/222, rerender-leak PASS, entry-points 427/427,
  locale-keys 23/23, a11y-name 43, a11y-description PASS, locale-switch 47/47, Components tsc clean.
- Plan deviation: a refused spot (inside the dragged node's own subtree) is simply not a target, with no indicator and
  no announcement, rather than "announced once" as plan step 2 said.

Pending: the human test; then, after telling Symbio (it asked to hear before v0.4.0 reaches Web main), merge, tag
v0.4.0 and push with the user's word, and Symbio TASK-1012.

## Human test, round 1 (2026-10-30, the user)

Playground "Tree menu, sortable" card on the branch: "playground was working". Steps 1 to 4 are recorded as one pass, as
the user reported them; touch on a real device and the screen reader were not reported separately. Step 5 (Symbio,
TASK-1012) follows the merge.

## Released (2026-10-30)

Birko.Web **v0.4.0**: merge 9e6b0fe on main, tagged and pushed at the user's word, after Symbio's go (its walk-through
on the dev server had to finish first). Symbio confirmed all four of its sortable trees already use `reorder`, so nothing
breaks; the arrows go, as its TASK-1012 wants. Playground merged and pushed (a0186f3; tree-drag-check 17/17 on main).
Worktrees and branches removed. **Open:** human test step 5 in Symbio (TASK-1012).
