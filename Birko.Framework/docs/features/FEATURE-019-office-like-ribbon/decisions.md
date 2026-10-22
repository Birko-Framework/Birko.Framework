---
id: FEATURE-019
created: 2026-10-22
---

# An Office-like ribbon (`b-ribbon`) — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Every group in the open ribbon is the same height, so the dividers between groups run the full height; commands sit in three rows that line up across groups; each group's name sits centred underneath it, as in Office. | approved | Picked "Full preview" of three variants shown live (heights + rows + labels below / heights + rows / heights only). | 2026-10-22 | user | TASK-730, TASK-743 |
| D2 | Inside the open ribbon, the arrow keys move between commands and from one group to the next, the way Office does. | approved | Picked "Yes, task it". | 2026-10-22 | user | TASK-731 |
| D3 | A command can open a menu of options (like Office's "Paste" with its small arrow): either the whole button opens the menu, or the main part runs the command and the arrow opens the menu. It looks like Office: the line between the two parts shows when the pointer is over it. A menu choice can show a small picture, its name and its keyboard shortcut, and choices can be grouped with dividing lines. | approved | Picked "Yes, task it"; then the Office look (option A of three shown live) and "icon, label, shortcut, separators" over "icon and label only" and "also submenus". | 2026-10-22 | user | TASK-732 |
| D4 | A group can show a small corner button (↘) next to its name that opens "more options" for that group. | approved | Picked "Yes, task it". | 2026-10-22 | user | TASK-733 |
| D5 | Some tabs appear only while something is selected (like Office's "Table Design" when a table is selected), shown together under a coloured heading, and disappear again when the selection goes. | approved | Picked "Yes, task it". | 2026-10-22 | user | TASK-734 |
| D6 | Double-clicking a tab collapses or expands the ribbon, and Ctrl+F1 does the same, as in Office. Ctrl+F1 works anywhere in the app; an app that uses that key for something else can switch it off or choose another key. | approved | Picked "Yes, task it". | 2026-10-22 | user | TASK-735 |
| D7 | A key combination shows a small letter on every tab; pressing it opens that tab and shows letters on its commands, so anything in the ribbon can be reached in two or three key presses. The combination is Alt+Windows key (Ctrl+Option on a Mac), as in Microsoft 365 in the browser; an app can choose another. | approved | Picked "Yes, task it". | 2026-10-22 | user | TASK-736 |
| D8 | A small row of favourite commands always visible in the tab row, before the tabs, which the user can add to and remove from, and which is remembered. An app can place it above the tabs or below the ribbon instead. | approved | Picked "Yes, both" (with rich tooltips, D9). | 2026-10-22 | user | TASK-737 |
| D9 | Hovering a command shows a proper tooltip with the command name, a one-line description and its keyboard shortcut, instead of the browser's plain label. It is the library's standard tooltip with a new light look (the ribbon uses the light look by default), and it also appears when a command is reached with the keyboard. | approved | Picked "Yes, both" (with the Quick Access Toolbar, D8). | 2026-10-22 | user | TASK-738 |
| D10 | A command can be on or off (like Bold), and shows its state: tinted with a thin outline while it is on. Commands can also form a set where exactly one is on (like left / centre / right alignment). | approved | Picked "Yes, all" (toggle buttons, galleries and inputs). | 2026-10-22 | user | TASK-739 |
| D11 | A group can show a row of visual choices (like Office's style gallery) that scrolls and expands into a larger grid. | approved | Picked "Yes, all" (toggle buttons, galleries and inputs). | 2026-10-22 | user | TASK-740 |
| D12 | A group can hold a small input or dropdown (like Office's font name and size boxes). | approved | Picked "Yes, all" (toggle buttons, galleries and inputs). | 2026-10-22 | user | TASK-741 |
| D13 | The desktop ribbon (Birko.Xaml.Avalonia) gets the same behaviours as the web one, after the web version. | approved | Picked "Web first, Avalonia later" over parallel or web-only. | 2026-10-22 | user | TASK-742 |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-10-22 — feature created from the user's ribbon review in the Playground (no grill interview: the user answered each item one by one). Decisions D1–D13 seeded as `proposed` and stamped in the same session from those answers.
- 2026-10-22 — D1 approved by the user (picked "full preview" of three variants shown live (heights + rows + labels below / heights + rows / heights only)); decomposed → TASK-730.
- 2026-10-22 — D2 approved by the user (picked "yes, task it"); decomposed → TASK-731.
- 2026-10-22 — D3 approved by the user (picked "yes, task it"); decomposed → TASK-732.
- 2026-10-22 — D4 approved by the user (picked "yes, task it"); decomposed → TASK-733.
- 2026-10-22 — D5 approved by the user (picked "yes, task it"); decomposed → TASK-734.
- 2026-10-22 — D6 approved by the user (picked "yes, task it"); decomposed → TASK-735.
- 2026-10-22 — D7 approved by the user (picked "yes, task it"); decomposed → TASK-736.
- 2026-10-22 — D8 approved by the user (picked "yes, both" (with rich tooltips, d9)); decomposed → TASK-737.
- 2026-10-22 — D9 approved by the user (picked "yes, both" (with the quick access toolbar, d8)); decomposed → TASK-738.
- 2026-10-22 — D10 approved by the user (picked "yes, all" (toggle buttons, galleries and inputs)); decomposed → TASK-739.
- 2026-10-22 — D11 approved by the user (picked "yes, all" (toggle buttons, galleries and inputs)); decomposed → TASK-740.
- 2026-10-22 — D12 approved by the user (picked "yes, all" (toggle buttons, galleries and inputs)); decomposed → TASK-741.
- 2026-10-22 — D13 approved by the user (picked "web first, avalonia later" over parallel or web-only); decomposed → TASK-742.
- 2026-10-22 — D1 built (TASK-730, awaiting sign-off). TASK-743 spawned from it: a folded group without an icon shows an empty button (it predates D1), and whether the ribbon keeps one height while groups fold.
- 2026-10-22 — D2 built (TASK-731, awaiting a screen-reader check).
- 2026-10-22 — D3 refined by the user from a live preview: the Office look (the divider shows on hover) over always-outlined and tinted; menu choices with icon, label, shortcut and separators, without submenus. Built (TASK-732, awaiting sign-off).
- 2026-10-22 — D9 refined by the user: use the library's existing tooltip with an added theme rather than a ribbon-only one; light theme by default for the ribbon; shown on hover delay and on keyboard focus. Built (TASK-738, awaiting sign-off). Escape now first hides a shown tooltip.
- 2026-10-22 — D10 refined by the user from a live preview: the pressed look is tinted and outlined (over tinted-only and an underline), and sets where only one can be on are in scope (open question Q4 answered). Built (TASK-739, awaiting sign-off).
- 2026-10-22 — D6 refined by the user (open question Q1): Ctrl+F1 works anywhere in the app, and an app can switch it off or rebind it, since some apps use the key.
- 2026-10-22 — D7 refined by the user (Q2), choosing from three suggestions: Alt+Windows key / Ctrl+Option, as in Microsoft 365 in the browser, over F10 (taken by browsers) and Alt alone (reliable only in an installed app); an app can change it.
- 2026-10-22 — D8 refined by the user (Q3) from a live preview of three positions: in the tab row before the tabs, over its own row above the tabs and below the ribbon; an app can switch the position.
- 2026-10-22 — D6 built (TASK-735, awaiting sign-off). Both triggers switch between the ribbon always shown and tabs only, as Office's "collapse the ribbon" does.
- 2026-10-22 — D8 built (TASK-737, awaiting sign-off): in the tab row by default, customised from a right-click menu and a ▾ menu, remembered per app.
- 2026-10-22 — D8 refined after the user asked whether the toolbar is optional: each app chooses to have one. Until it does, no toolbar and no "Add to Quick Access Toolbar" on a right-click.
- 2026-10-22 — D7 refined by the user from a live preview: dark squares as in desktop Office (over a light card and an accent colour), and letters chosen automatically unless an app sets its own. Built (TASK-736, awaiting sign-off).
- 2026-10-22 — D4 refined by the user from a live preview: Office's small corner arrow beside the group's name (over "more" dots and a clickable name). Built (TASK-733, awaiting sign-off).
- 2026-10-22 — D5 refined by the user from a live preview: all three looks kept (a tinted band with the set's name, a coloured pill, a coloured line), chosen per app or per set; when the open set disappears, the ribbon goes back to the tab open before it. Built (TASK-734, awaiting sign-off).
- 2026-10-22 — D11 refined by the user from a live preview: all three looks kept (a framed row with small buttons, an open row with arrows, a single dropdown tile), chosen per app or per gallery; picking a tile makes it the current choice. Built (TASK-740, awaiting sign-off).
