---
id: TASK-543
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
picked-by: fix-next
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-021]
pr: null
github-issue: null
jira-key: null
---

# b-code-block does not re-render when its text content changes

## Context

**Field report (2026-10-08, Presenter consumer).** Navigating a Presenter deck from one slide to the
next, where both slides open with a fenced C# code block, kept showing the **first slide's code** on the
second slide. Reproduced in `C:\Source\Birko\Consumers\Presenter` with
`docs/talks/02-birko-framework.md` slides 7 → 8 → 9; a full page reload shows the correct code. The
server-side deck JSON was checked and carries different code for each slide — the defect is client-side.

**Mechanism.** `b-code-block` (`Birko.Web.Components/src/data/b-code-block.ts`) reads its source in
`render()` via `_getCode()` — the `code` attribute, else `this.textContent`. `render()` only runs on
connect and on an **observed attribute** change (`language`, `code`, `wrap`, `show-line-numbers`,
`no-copy`, `max-height`, `size`, `sticky-header`). A change to the element's **light-DOM text** triggers
nothing.

`BaseComponent` (`Birko.Web.Core/src/base/base-component.ts`) applies a parent's re-render by **DOM
morphing** (`_morphChildren`), which deliberately keeps existing child elements to preserve custom-element
state. When two consecutive renders put a `<b-code-block>` with identical attributes at the same position,
the morph keeps the element and replaces only its text node — so the block keeps displaying the previous
highlighted code. Any consumer that morphs content containing `b-code-block` can hit this, not only
Presenter.

The consumer-side workaround is tracked separately in Presenter **TASK-014** (force a full re-render of
slide content on slide change). This task is the root-cause fix in the component.

## Acceptance criteria

- [x] `b-code-block` re-renders when its light-DOM text content changes (e.g. a `MutationObserver` on
      `childList` + `characterData` + `subtree`, connected/disconnected with the element), and the
      displayed, highlighted code matches the new text
- [x] The observer does not fire on the component's own shadow-DOM render (no render loop), and is
      disconnected in `disconnectedCallback`
- [x] The `code` attribute keeps precedence over text content, as today
- [x] The copy button copies the **current** code after a content change (`_code` is refreshed)
- [x] A Playground harness check (`verify.mjs`) reproduces the morph case — same element, attributes
      unchanged, text swapped — and asserts the rendered code updates; shown to **fail** before the fix
- [x] Existing Playground checks still pass (`verify.mjs`, cross-engine)

## Out of scope

- Changing `BaseComponent`'s morph strategy — the morph's element reuse is intended behaviour
- Auditing every other `b-*` component that reads light-DOM content for the same pattern — if one is
  found, it gets its own task
- The Presenter-side workaround (Presenter TASK-014)

## Human test plan

- [x] In the Playground code-block page, swap a block's text in place from devtools
      (`el.firstChild.textContent = 'var x = 1;'`) → the rendered, highlighted code updates immediately
- [x] After the fix lands, in Presenter run `docs/talks/02-birko-framework.md` and step 7 → 8 → 9 with
      Presenter TASK-014's workaround reverted (or before it lands) → each slide shows its own code
      — *run 2026-10-08 by the owner on Presenter `main` (TASK-014's workaround is only on its unmerged
      `task/TASK-014` branch), SPA rebuilt against this fix, deck served locally: 7 → 8 → 9 → 8, every slide
      showed its own code*

## Implementation plan

Planned and executed inline by fix-next; see Progress log and Outcome.

## Progress log

- step 2 — picked at the user's request (2026-10-08) while TASK-140 awaits Symbio sign-off; field defect (FIELD-021) reachable by any consumer whose parent re-render morphs a `b-code-block`; Presenter works around it locally (its TASK-014), nobody else had it in flight
- step 3 — verified: holds. `b-code-block.ts` reads `textContent` in `render()`, which runs only on connect and on its 8 observed attributes; `BaseComponent._morphChildren` keeps an element whose attributes match and replaces only its text node. Reproduced by the harness before the fix (shows "var first = 1;" after the host re-rendered with "var second = 2;")
- step 4 — layer: local (Birko.Web.Components, in the `Birko\Web` checkout); Presenter's `data-morph="skip"` (its TASK-014) is a consumer guard, not a copy of this fix
- step 5 — fix in `Web/Birko.Web.Components/src/data/b-code-block.ts` (light-DOM `MutationObserver`, `onMount`/`onUnmount`); checks in `Consumers/Birko.Web.Playground/src/backport-smoke.ts`; `node verify.mjs` backport-smoke 324/324, 0 failing; `cross-engine-check.mjs` chrome 113/113, firefox 113/113 (the KNOWN TASK-466 line is unrelated)
- step 6 — reverted b-code-block.ts to HEAD: 3/5 failed; fix-dependent = "parent morph keeps the element and the block shows the new code" (old: same element, shows "var first = 1;"), "in-place text edit re-renders" (old: "var a = 1;"), "copy uses the current code"; contract pins (pass either way, labelled "(contract pin)") = code attribute precedence, a disconnected block does not observe
- step 7 — no spec area (Birko.Web.* is on `docs/specs/.map.yml:81`'s uncovered list). Docs: Birko.Web.Components README `b-code-block` section
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log

## Outcome

**What was fixed.** `b-code-block` drew its code from its text content but only re-rendered when one of its attributes
changed. When a parent component re-rendered, Birko's DOM morph kept the existing `<b-code-block>` (same attributes) and
swapped only its text — so the block kept showing the old code. In Presenter that meant slide 8 showing slide 7's code.
The block now watches its own light DOM and re-renders when the text changes.

**Proof.** The harness drives the real morph through a small `BaseComponent` host: the element is kept, its text is
swapped. Reverting the fix fails 3 of 5 checks — the morph case, an in-place text edit, and the copy value — each
printing the stale code it showed. The other 2 are contract pins (the `code` attribute still wins; a removed block does
not observe), labelled as such.

**Judgement calls.**
- *Fix in the component, not the morph.* The morph keeping elements is how child state survives a re-render; the
  component that reads light DOM is the one that must notice its light DOM changed.
- *Skip when `code` is set or the text equals what was last rendered.* Keeps the attribute's precedence and avoids a
  render per keystroke-sized mutation that changes nothing.
- *Observer lives in `onMount`/`onUnmount`,* so a reconnected element re-observes and a detached one costs nothing.

**Sign-off (2026-10-08, owner).** The field repro passed in Presenter without its own workaround: on `main`, rebuilt
against this fix, slides 7 → 8 → 9 → 8 each showed their own code. The first plan step is automated by "in-place text
edit re-renders". Closed `verify → done`. Presenter's TASK-014 (`data-morph="skip"` on slide containers) is no longer
needed for this bug; whether to keep it as a general guard is Presenter's call.

**Flagged, not fixed.** Other `b-*` components that read light-DOM content may share the pattern — already a boundary in
Out of scope, which says a found one gets its own task; none was looked for here.
- verify → done — owner sign-off 2026-10-08: field repro in Presenter (main, no TASK-014 workaround) passed
