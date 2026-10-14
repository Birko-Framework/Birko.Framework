---
id: TASK-545
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-09
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# A reversible delete can offer Undo instead of a confirm dialog — toast action + base-page opt-in

## Context

The generic UX baseline (`project-lifecycle-skills`, `skills/verify-conventions/ux-baseline.md`, its TASK-293)
has a row *"Confirm guarding a reversible action → do it at once and offer Undo"*. A Birko.Web consumer **cannot
follow it today**, because the framework has neither half of the mechanism:

- **`b-toast` has no action.** `ToastManager._add(container, message, variant, durationMs, href?, icon?)`
  (`Birko.Web.Components/src/feedback/b-toast.ts:69`) renders the message and a close `×` (`:143`). `NotifyOptions`
  carries `durationMs`, `href`, `icon` and nothing a user can click to act.
- **The base pages always confirm.** `base-crud-page._confirmDelete` (`Birko.Web.Shell/src/pages/base-crud-page.ts:907`)
  shows `<b-confirm-dialog id="confirm">` and then calls `api.delete`. `base-list-page` (`:119`, `:160`) and
  `base-split-page` (`:313`, `:486`) route through the same flow. The shared string is
  `'bws.common.confirmDelete': 'Delete this {entity}? This cannot be undone.'` (`:277`) — which is false for a
  consumer whose entities soft-delete.

Measured in the main consumer (Symbio, 2026-10-09): **78 `confirmDelete(` + 38 `confirm(` calls**, while every
Symbio entity soft-deletes (`BaseEntity : ISoftDeletable`) and `IRepository.RestoreAsync(id)` already exists. The
backend can take a delete back; the UI has no way to offer it. The consumer half is Symbio TASK-899, which
waits on this one.

**The undo must be real.** An Undo button is a promise that the action is reversed, so the opt-in takes a
**restore callback the page supplies**, never a client-side "pretend it is still there". A page without one keeps
the confirm. This is rule 51's shape on the UI side: a setting that offers what the mechanism cannot deliver is
worse than no setting.

## Acceptance criteria

- [x] `toast` accepts an **action** (`{ label, onClick }`) on both `show` and `notify` paths; it renders as a real
      `<button>` beside the message, clicking it runs the callback and dismisses the toast
- [x] A toast with an action is reachable and operable from the keyboard, is announced to screen readers
      (live region carries the action's existence), and **does not auto-dismiss while it holds focus or hover**
- [x] Action label is translatable — the default Undo text lives in the `bwc.*` key tree, not hard-coded
- [x] `base-crud-page` (and through it `base-list-page`, `base-split-page`) gains an **opt-in** undo-delete mode:
      the page supplies a restore function; delete runs at once, the row leaves the view, and a toast offers Undo
      that calls the restore and refreshes. **Default stays the confirm dialog** — no existing page changes
- [x] A page that opts in without supplying a restore function **fails loudly** (throws at setup), never silently
      falls back to an Undo that cannot undo
- [x] A restore that fails (non-2xx) shows an error toast; the row does not reappear as if it had been restored
- [x] Tests: toast action click / keyboard / focus-holds-open; base page in both modes; the missing-restore guard;
      the failed-restore path
- [x] `Birko.Web.Components/CLAUDE.md` and `Birko.Web.Shell/CLAUDE.md` document the action option and the opt-in,
      including *when not to use it* (hard deletes, money, permissions — keep the confirm)

## Out of scope

- Migrating any consumer's confirm calls — Symbio TASK-899, which depends on this one
- A server-side `Restore` on `Birko.Data.Patterns`' soft-delete wrappers (`SoftDeleteStoreWrapper` marks
  `DeletedAt` but has no restore). Worth its own task if a second consumer needs it; Symbio has its own
  `RestoreAsync`
- Delayed-commit undo (delete only after the toast expires) — a different mechanism with its own failure modes
  (tab closed mid-window); only if a consumer without soft-delete asks for it

## Human test plan

Automated: Playground `undo-delete-smoke` 41/41 and `keyboard-check` 27/27 (10 new, trusted input), `verify.mjs` and
`entry-points-check` green; the guard, the focus/hover pause and the keyboard-only focus move each proven red by
breaking them. What is left needs a real backend and a screen reader:

- [ ] In a consumer page with the opt-in: delete a row, click Undo — the row is back, and a reload confirms it
      was restored server-side, not just redrawn
- [ ] Same with keyboard only: Tab to the toast's Undo, press Enter
- [ ] With a screen reader (NVDA): the toast announces the message and that an action is available
- [ ] Hover the toast past its duration — it stays; move away — it dismisses

## Implementation plan

Drafted at pick, 2026-10-14. Code lands in `Birko\Web` (one commit), the smoke suites in
`Birko.Web.Playground` (one commit), this file in the framework repo.

1. **`b-toast` action** (`Birko.Web.Components/src/feedback/b-toast.ts`)
   - `ToastAction { label?: string; onClick: () => void | Promise<void> }`. `show(message, variant, durationMs?,
     { action })` and `NotifyOptions.action`. Both return the `BToastItem` so a caller can move focus to it.
   - The item renders the action as `<button type="button" class="action">` inside the existing `role=status`
     region, so the announcement carries it. Label escaped; default `bwc.common.undo` ("Undo") via `label()`.
   - Click: stop propagation (no `href` navigation), dismiss, then run the callback (a throw is logged, never
     swallowed silently).
   - **Dismissal moves into the item** (`autoDismiss(ms)`): the timer pauses on `pointerenter` / `focusin` and
     resumes on leave with what was left, never less than 1 s. Applies to every toast (WCAG 2.2.1), not only
     action ones. Default duration with an action is 10 s (4 s without).
   - `focusAction()` on the item.
2. **Undo-delete opt-in** (`Birko.Web.Shell/src/pages/base-crud-page.ts`)
   - `protected deleteMode: 'confirm' | 'undo' = 'confirm'` and `protected restoreDeleted?(id): Promise<ApiResponse>`.
   - Guard: `connectedCallback` throws when `deleteMode === 'undo'` and there is no `restoreDeleted`
     (subclass fields are not set yet in the constructor, so this is the earliest point).
   - `_confirmDelete` branches: in undo mode it skips the dialog, deletes, runs `onDeleteSuccess` + `_afterDelete`
     (the row leaves), and shows `bws.common.deleted` with an Undo action. Undo → `restoreDeleted(id)`; ok →
     `bws.common.restored` toast + `_afterRestore(id)` (default `reload()`); non-ok or throw → error toast, no reload.
   - A keyboard-initiated delete (`:focus-visible` on the deep active element at the start) moves focus to Undo,
     because the control that had focus is about to disappear with its row.
   - `base-list-page` / `base-split-page` route through `_confirmDelete` already; nothing else changes there.
3. **Tests** (Playground): `undo-delete-smoke.ts` (toast action click, dismissal, label default + i18n, focus
   holds it open, base page in both modes against a fake `ApiClient`, the missing-restore guard, the failed
   restore) registered in `verify.mjs`; `keyboard-check.mjs` gains trusted Tab-to-Undo + Enter and hover-holds-open.
   Prove a guard red by reverting it once.
4. **Docs**: Components `CLAUDE.md` / `README.md` / `API.md` (action option), Shell `CLAUDE.md` / `README.md`
   (opt-in, and when not to use it), `locales/en.json`.

## Progress log

- 2026-10-14 — Built. Web `7b5d213` (b-toast action + pause; base-crud-page `deleteMode`/`restoreDeleted`; docs;
  `bwc.common.undo`, `bws.common.restored`), Playground `d1969ec` (smoke + keyboard-check). Not pushed or tagged: the
  Web repo tags on push (its CLAUDE.md § Versioning), so this rides the next release.
- Behaviour change for **every** toast, recorded here because it is wider than the opt-in: dismissal now pauses while
  a toast is hovered or holds focus, and resumes with what was left (never under 1 s). `show()` / `notify()` return the
  item instead of `void`, which no caller can observe.
- Spawned [[TASK-597]]: the toast's `message` and `icon` are rendered as HTML, and pages pass server error text to it.
