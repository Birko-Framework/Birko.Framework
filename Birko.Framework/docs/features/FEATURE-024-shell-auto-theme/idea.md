---
id: FEATURE-024
created: 2026-11-22
owner: both
# status — one of: idea, review (built, sign-off pending), done, dropped, superseded
status: done
---

# App shell: an "Auto" theme that follows the browser's light or dark setting

> Stakeholder-readable. A project manager or end user should understand the problem and the proposed shape without reading any code.

## Problem

Asked for by DraCode (2026-11-22, for its TASK-188, a Settings choice of light, dark or auto). The app shell picks its
theme when the app loads: the one the user chose last, otherwise light. It never looks at the browser's own light or
dark setting, so someone whose browser and system are set to dark still gets the light app until they change it by
hand.

DraCode's request, as sent: "(1) setTheme('auto') (and a saved 'auto'): apply dark or light from
matchMedia('(prefers-color-scheme: dark)'), and switch live when that media query changes. currentTheme should still
report 'auto'. (2) Ideally, the default when nothing is saved can be 'auto', or is configurable per app. (3) The header
theme menu offers Auto."

## Proposed shape

Grilled 2026-11-22; see decisions.md. An "Auto" choice next to Light and Dark: the app follows the browser's setting, and changes when the
setting changes while the app is open. The app remembers that the user chose Auto, not the theme Auto picked. An app
can make Auto its starting theme for users who have not chosen one.

## Open questions

| # | Question | State |
|---|---|---|
| Q1 | Does Auto become every app's starting theme, or does each app opt in (Symbio's users would otherwise switch to dark if their system is dark)? | answered → D2 |
| Q2 | Apps can register their own themes beyond light and dark. Which two does Auto choose between: always light and dark, or a light/dark pair each app names? | answered → D3 |
| Q3 | How the theme menu shows Auto: its label, its place in the list, and whether it says which theme it is showing now. | answered → D4 |
| Q4 | Before the app has loaded, the page shows the default look. Should a dark-set browser get dark from the first paint, with no flash of light? | answered → D5 |

## Out of scope (initial)

- Following the system's high-contrast setting.

## Prototype

Not built yet.
