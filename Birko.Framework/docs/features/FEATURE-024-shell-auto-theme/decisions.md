---
id: FEATURE-024
created: 2026-11-22
---

# App shell: an "Auto" theme that follows the browser's light or dark setting — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | The app shell offers an "Auto" theme that follows the browser's light or dark setting and changes when that setting changes while the app is open; the app remembers that the user chose Auto, not the theme it showed | approved | DraCode's request (its Settings offer light, dark or auto); the shape it asked for | 2026-11-22 | František Bereň (maintainer) | TASK-971 |
| D2 | Auto is a starting theme only for apps that ask for it; others still start in light | approved | Chosen in the grill over Auto for every app: Symbio's users with a dark system would otherwise switch to dark unasked | 2026-11-22 | František Bereň (maintainer) | TASK-971 |
| D3 | Auto chooses between light and dark, and is offered only in apps that have the dark theme | approved | Chosen in the grill over a light/dark pair each app names: simpler, and dark is the only dark theme the framework ships | 2026-11-22 | František Bereň (maintainer) | TASK-971 |
| D4 | The theme menu shows "Auto" at the top of its list, ticked when chosen, translatable like the other theme names | approved | Chosen in the grill over "Auto (Dark)", which names the theme shown now | 2026-11-22 | František Bereň (maintainer) | TASK-971 |
| D5 | The framework provides a short documented snippet an app can put in its page head so a dark browser gets dark from the first paint; apps that skip it may see a brief flash of light | approved | Chosen in the grill over accepting the flash for now | 2026-11-22 | František Bereň (maintainer) | TASK-972 |

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-11-22 — feature created from DraCode's request (docs/BRIEF.md, 2026-11-22). Q1-Q4 grilled with the maintainer
  in the session's picker the same day; D1 is the request itself, D2-D5 approved as recommended.
- 2026-11-22 — Feature reviewed and done: D1-D5 built in TASK-971..972 and shipped in Birko.Web v0.5.2;
  every human test plan run and signed off by the maintainer in the Playground ("all passed").
