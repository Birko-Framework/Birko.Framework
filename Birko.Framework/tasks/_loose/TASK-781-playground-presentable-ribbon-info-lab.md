---
id: TASK-781
parent: null
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-10-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Make the published Birko.Web.Playground presentable: ribbon header, per-component info, a separate Lab

## Context

The user asked (2026-10-28) for the public gallery at https://birko-framework.github.io/Birko.Web.Playground/ to "look
better, presentable": each element should say what it is and what it does (via tooltip?), the menu could be a ribbon,
and the elements that exist for testing only should be somewhere separate. Later in the same session: the "Birko.Web —
the component library, running" panel should not be there always — move it into Help or suggest something.

Before, the header was one row of a category `b-segmented`, theme and locale switches and two buttons; each card showed
only `Label <b-tag>`; four test harnesses sat among the components; the intro panel sat above the gallery on every
visit until dismissed.

## Decisions (the user's picks, 2026-10-28, from previews)

| # | Decision | State |
|---|---|---|
| D1 | Header is a `b-ribbon` with tabs **by task**: Gallery (category set + Find), Appearance (theme, locale, tokens, CSS), Lab, Help | approved |
| D2 | Each card: **one visible line** saying what it is + an **ⓘ tooltip** with what it does, its attributes, methods and events, from Birko.Web.Components' README/API.md | approved |
| D3 | **Lab** holds the grid benchmark, device utils, the help-text row check and the page-tour button check; equalizer, dialogs and mobile shell stay as demos | approved |
| D4 | The intro moves to **Help → About** (a modal); a first visit gets **one slim dismissible line** pointing at it | approved |

## Acceptance criteria

- [x] The header is a `b-ribbon`; every command has a description tooltip; theme and locale sets reflect the active one
- [x] Every one of the 75 catalogue cards shows an ABOUT line; `reportMissing()` warns about an entry without one
- [x] The ⓘ tooltip is generated at build time from the library's README.md and API.md (`component-docs.mjs`), not copied
- [x] The four harnesses render under Lab, behind a banner saying they are not components; they no longer count in the
      gallery categories
- [x] Find filters the shown section by name, tag or description
- [x] The intro is Help → About; the first-visit hint is one line and stays dismissed
- [x] Every Playground check passes: verify.mjs (75 cards, 0 failing), subpath, theme-roundtrip 11/11, device-fix
      140/140, keyboard 222/222, ribbon-a11y 22/22, a11y-name 43, a11y-description, entry-points 409/409, locale-keys 20/20

## Human test plan

On the published page (after the push) or `node build.js` and serve `wwwroot/`:

1. First visit (clear site data): one blue line under the ribbon, not a panel. "About this playground" opens the dialog
   and the line does not come back on reload. ✕ also dismisses it.
2. Ribbon → Gallery: the six category buttons switch the section; the pressed one follows. Hover one: a tooltip says
   what the category holds.
3. Type `date` in Find and press Enter: only the three date components remain. Clear shows all again.
4. Hover or Tab to ⓘ on a few cards (Button, JSON diff, Ribbon, a Lab card): the tooltip opens to the left, on screen,
   and reads sensibly — no cut-off sentence, no task numbers.
5. Ribbon → Appearance: switch Dark / Neon / Inverse and CS — the gallery restyles, the pressed button follows. Design
   tokens opens the drawer, Generate CSS the modal. Pick SK and reload: still SK (Quantity stepper shows 1,5 and
   "Znížiť"), SK pressed.
6. Ribbon → Lab: the banner and the four harnesses; Gallery returns to the category you left. "Run smoke suites" opens
   `?smoke=1` in a new tab.
7. Ribbon → Help: About, Tour (three steps, replays every time), and the four docs links open in new tabs.
8. Layout → Tour → "Start tour" runs the demo tour (it threw before: the demo called `start()` on the element).
9. At phone width the ribbon collapses to its mobile menu and the page does not scroll sideways.

## Outcome (2026-10-28)

Birko.Web.Playground: `src/app.ts` (ribbon header, `ABOUT` / `CATEGORY_ABOUT`, ⓘ tooltips, Lab category and banner,
Find filter, About modal + hint, the playground tour), new `component-docs.mjs` + `src/pg-modules.d.ts`, `build.js`
plugin. verify.mjs, subpath-check.mjs and theme-roundtrip-check.mjs dispatch the ribbon's `item-click` instead of the
removed `#pg-nav` / `#open-tokens`. The header ribbon turns off its page-wide Ctrl+F1 and key-tip shortcuts, which
otherwise took them from the gallery's Ribbon card (keyboard-check found it: 7 FAIL, then 222/222). Also fixed: the
gallery's `b-tour` demo called `el.start()`, which `BTour` does not have (`start` is the `tour` manager's).
The Locale pick now persists under `pg-locale` (asked for after review: it reset to English on reload), written
only by the ribbon click and skipped under `?smoke` / `?kb`. Checked in Chrome on localhost (light and dark, tooltips, Lab, About, both tours). Human sign-off pending.
