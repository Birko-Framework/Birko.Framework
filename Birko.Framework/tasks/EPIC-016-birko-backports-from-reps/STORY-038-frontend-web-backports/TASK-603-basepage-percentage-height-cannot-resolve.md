---
id: TASK-603
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-041]
pr: null
github-issue: null
jira-key: null
---

# `BasePage`'s `:host { height: 100% }` cannot resolve in the shell's own recommended structure

> **Moved from WorkoutTracker TASK-152 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. There, TASK-152 stays open as the Reps-side check, blocked on this task.


> **P3 → P2, 2026-08-08 (roadmap re-rank).** CLAUDE.md's rule for bottom-anchored UI is "fix the shell, don't
> measure around it". Leaving this at P3 keeps TASK-133's per-consumer `height: 100%` on the router outlet
> load-bearing, which is exactly the second consumer of shell geometry the convention warns about.

## Context

Found while building **TASK-133** (pin the session's action row), 2026-08-06. The task's implementation plan
reasoned down a percentage-height chain — `BasePage`'s `:host { height: 100% }` → the page's content → the
shell's `.mobile-content` scroll container — and the plan was right about every link except whether the chain
was connected. It is not, and has never been.

`BCoreAppShell`/`BMobileAppShell` give `.mobile-content` a definite height (`flex: 1` inside a
`height: 100dvh` column flex, `overflow-y: auto`). `BasePage` declares `:host { display: block; height: 100% }`
(`Birko.Web.Shell/src/pages/base-page.ts:73`), which reads as "a page is as tall as the band it is rendered
into". Between them, in the structure the shell itself prescribes, sits the consumer's **light-DOM router
outlet** — a plain `<div>` with `height: auto`, projected through the shell's `<slot>`:

```html
<reps-app>
  <div id="router-outlet"></div>   <!-- height: auto -->
</reps-app>
```

A percentage height resolves against the parent's height. The parent is `auto`. So `:host { height: 100% }`
computes `auto` on **every page of every consumer built this way**, and has since the shell shipped. Measured in
Reps: `#router-outlet` 560.5px only *after* the fix; before it, the session page could not size itself against
the viewport at all, and a sticky action row keyed to the bottom of the band still drifted 73.8px.

**Nothing was visibly broken by this**, which is why it went unnoticed for months: no page had yet asked for a
percentage height. The declaration simply had no effect. The failure mode is not a wrong layout, it is a
**declaration that lies** — the first consumer to trust it debugs the wrong thing, which is precisely what
happened, and it cost a full measurement pass to find.

## Reps has already fixed itself

`Reps.Web/index.html` now sets `#router-outlet { height: 100% }`, with the reasoning inline, and `CLAUDE.md`
§ Architecture records it. That is correct consumer-side: the outlet is the consumer's own markup, so the shell
cannot style it from where it stands today. **This task is about the other consumers and the next one**, not
about Reps.

## Change

Decide which of these the framework should do — the choice is the work, and it is not obvious:

1. **Document it.** `BasePage`'s doc comment and the shell's usage example state that the outlet must carry
   `height: 100%` for the page's own height to resolve. Cheapest; relies on being read.
2. **`::slotted(*) { height: 100% }` on `.mobile-content`.** The shell sizes whatever it is given. Correct for
   the mobile shell's single-outlet contract, but it reaches into consumer markup for *every* slotted child, and
   a consumer slotting more than one element gets a surprise.
3. **Have the shell own the outlet.** The `Router` renders into an element the shell provides, rather than one
   the consumer writes into `index.html`. Cleanest and the largest change; touches every consumer's bootstrap.
4. **Drop the declaration.** If no page is meant to fill the band, `:host { height: 100% }` is noise and should
   go — the honest option if (1)–(3) are all judged not worth it.

Option 2 is the likely answer for `BMobileAppShell` specifically; option 1 is the floor. Whatever is chosen,
**a declaration that cannot take effect in the prescribed structure should not survive this task in that state.**

## Acceptance criteria

- [ ] A page that declares a percentage height resolves it against the shell's scroll container, in the
      structure the shell's own documentation prescribes — or the declaration is removed and that is documented.
- [ ] Whichever option is taken is recorded where a consumer will meet it (`BasePage`'s doc comment and/or the
      shell usage example), not only in this task.
- [ ] Existing consumers are checked for a layout change before/after — a page that has been `auto`-height
      since it shipped may be relying on that without knowing.
- [ ] Covered in `Birko.Web.Playground`'s `backport-smoke.ts` (there is no in-framework JS unit runner), per the
      STORY-009 convention.

## Human test plan

- [ ] In the Playground, a page declaring `height: 100%` fills the content band and does not clip when its
      content grows past it.
- [ ] Each existing consumer's pages render unchanged — this is the risk of options 2 and 3, and it is not
      something the Playground can answer for a consumer it does not contain.

## Why P3

Nothing is broken on screen today in any consumer, and the one consumer that needed it has fixed itself. The
cost is paid by the *next* person who trusts the declaration — which is real, and is exactly what TASK-133 paid,
but it is not a defect a user can see.
