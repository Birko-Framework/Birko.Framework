---
id: TASK-945
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
related: [TASK-943]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-chat` escapes its placeholder and send label

## Context

Reported by DraCode (its TASK-151, the docked Dragon on `b-chat`), 2026-11-21. `b-chat`'s `render()` put the
placeholder (`ChatConfig.placeholder` or the `placeholder` attribute) into `placeholder="…"` and `aria-label="…"`, and
`ChatConfig.sendLabel` into the send button, all unescaped. A `"` closed the attribute; a send label could add an
element with a handler. The file already imported `escapeHtml` and used it only for message content.

The values usually come from the app, so the risk is lower than [[TASK-943]]'s, but `b-chat` is reusable and some
apps pass translated or user-supplied text.

## Change (Birko.Web v0.4.20, with TASK-943 and TASK-944)

- `render()` escapes both values once, with the file's `escapeHtml`.
- **Consumer-visible:** a consumer that passed already-escaped text (to `setConfig`, or by setting the attribute from
  code) now gets it escaped twice (`&amp;` shown). Documented in the Components README and the CHANGELOG. Escaping the
  value inside one's own HTML markup is not that case: the browser decodes it when parsing, so `b-chat` reads plain
  text. DraCode does only the latter and needs no change (its session confirmed, 2026-11-21).

## Evidence

backport-smoke: 3 checks (placeholder via attribute, via config, send label) fail before the change, with the send
label producing an `<img onerror>`; 400/400 on 3 of 3 runs with it, 0 failing checks. rerender-leak PASS, a11y-name OK,
Components `tsc` clean.

## Acceptance criteria

- [x] A placeholder or send label containing `"`, `&` or markup shows as that text and adds no attribute or element.

## Human test plan

1. In a page with `b-chat`, set `placeholder` to `Ask "anything" & more`: the input shows exactly that text.
2. A consumer that passes already-escaped text checks its labels after moving to v0.4.20: no `&amp;` or `&quot;`
   visible.

## Verified (2026-11-21)

The user ran the human test plan in the Playground (built from the task branch) and confirmed it; the same inputs were
then checked in Chrome: no alert and no `on*` attribute after hover/focus on every preview element, no `javascript:`
link, the `b-chat` placeholder and `aria-label` equal to the hostile string as text, the send label as text.
Merged into Birko.Web main (be2be45), tagged v0.4.20 locally.
