---
name: roll-changelog
description: Prune the "Recent Updates" section in `Birko.Framework/CLAUDE.md` by moving the oldest entries into `CHANGELOG.md`. Use when the user says "roll changelog", "prune recent updates", "move oldest entries to CHANGELOG", "uprac CLAUDE.md", "presun do CHANGELOG", or notes that `Recent Updates` has grown too large. Reads CLAUDE.md, identifies the oldest N `### Title (YYYY-MM-DD)` entries, converts them to the CHANGELOG format `## YYYY-MM-DD — Title`, prepends them to CHANGELOG.md (under the intro paragraph, above existing entries — newest-first within CHANGELOG), and deletes them from CLAUDE.md. Mirrors what the user did manually in commit `0f51a01` ("docs: backfill CHANGELOG with 5 entries from CLAUDE.md Recent Updates").
---

# Birko Framework — Roll Recent Updates into CHANGELOG

The `## Recent Updates` section at the bottom of `C:\Source\Birko.Framework\CLAUDE.md` is a rolling log of architectural changes. It grows on every change and would eventually bloat the project instructions (which Claude reads on every invocation). When it has more than ~5–8 entries, the oldest entries should be moved to `CHANGELOG.md` for long-term preservation while keeping `CLAUDE.md` focused on the latest changes.

This is the same manual chore the user performed in commit `0f51a01` (*docs: backfill CHANGELOG with 5 entries from CLAUDE.md Recent Updates*).

## Authoritative references — READ BOTH FILES FIRST when invoked

- `C:\Source\Birko.Framework\CLAUDE.md` — source of entries. Locate the `## Recent Updates` heading (near the bottom of the file). Entries below it have the form `### Title (YYYY-MM-DD)` followed by a paragraph and bullet list.
- `C:\Source\Birko.Framework\CHANGELOG.md` — destination. Look at the existing format: `## YYYY-MM-DD — Title` (em-dash `—` between date and title, NOT a hyphen), then a paragraph, then bullets. Existing entries are sorted **newest-first**.

If either file's structure has evolved since this skill was written, **follow the files** — this skill is a recipe, not a spec.

## Inputs to gather

1. **How many entries to roll** — default to **all entries older than 30 days** from today (`2026-05-26` was the date when this skill was authored — use today's actual date via the conversation context, not this literal). Offer alternatives: "oldest 3", "oldest 5", "everything before YYYY-MM-DD", "everything except the most recent N".
2. **Confirm the cutoff** — list the entries that will be moved and ask the user to confirm before mutating files.

## Format conversion

CLAUDE.md "Recent Updates" entries look like:

```markdown
### Birko.Web.Components — b-date-range-picker (2026-05-26)
Added `<b-date-range-picker>` (inputs now 21, total components 55). New input for selecting a date range with two endpoints in one panel.
- **Two-month side-by-side panel** by default; `months-visible="1"` for narrow viewports. ...
- ...
```

CHANGELOG.md entries look like:

```markdown
## 2026-05-26 — Birko.Web.Components — b-date-range-picker

Added `<b-date-range-picker>` as the 21st input component (total components now 55). Selects a date range with two endpoints in one panel; the API mirrors existing `b-*` input conventions ...

- **Two-month side-by-side panel** by default; `months-visible="1"` for narrow viewports. ...
- ...
```

Conversion rules:

1. **Heading** — `### {Title} ({YYYY-MM-DD})` becomes `## {YYYY-MM-DD} — {Title}`. The em-dash is `—` (U+2014), not a hyphen.
2. **Add a blank line** after the heading (CLAUDE.md often inlines the first paragraph; CHANGELOG.md separates them).
3. **Add a blank line between heading text and the first bullet** if not already present.
4. **Body content** — copy verbatim. Do NOT rewrite or summarize. The user already wrote the prose carefully when adding the entry to `Recent Updates`.
5. **Sort order in CHANGELOG.md** — entries within CHANGELOG.md are **newest-first**. When prepending multiple rolled entries, preserve their relative order (oldest of the rolled batch goes furthest down, newest of the rolled batch goes immediately under the existing newest entry in CHANGELOG.md).
6. **Separator** — entries in CHANGELOG.md are separated by a horizontal rule (`---`) per the existing file's convention. Check the file before assuming.

## Procedure

1. **Read both files** in full.
2. **Parse the `Recent Updates` section** of CLAUDE.md — identify each `###` entry, extract its date and title.
3. **Sort by date** ascending. Pick the entries to roll based on the user's choice in step 1 of "Inputs to gather".
4. **Show the user** the list of entries that will be moved (just titles + dates), and confirm before mutating.
5. **For each entry**, in oldest-first order:
   - Reformat its heading per the rules above.
   - Insert it into CHANGELOG.md **just below the intro paragraph** and **above** the current newest entry (so CHANGELOG remains newest-first).
   - Add the `---` separator if the file uses one between entries.
6. **Delete the entries** from CLAUDE.md `Recent Updates`. Be careful with leading/trailing blank lines — don't leave double-blanks or strip the section heading.
7. **Verify** by reading both files back and checking:
   - CLAUDE.md still has the `## Recent Updates` heading and the un-rolled entries.
   - CHANGELOG.md has the new entries in the right place with correct format.
   - No content was lost or duplicated.

## What this skill does NOT do

- It does not commit. After mutation, leave the diff for the user to inspect (`git diff CLAUDE.md CHANGELOG.md`) and commit themselves. Per [[feedback_no_coauthor]] the commit message must not include the `Co-Authored-By: Claude` line if you are asked to commit.
- It does not rewrite or paraphrase entries. Format conversion only.
- It does not delete entries from `CHANGELOG.md` — that file is append-only history.
- It does not touch the `## Architecture` or other sections of CLAUDE.md.

## Edge cases

- **Entry has no date in heading** — rare but possible. Ask the user for the date; do not guess from git log (git timestamps may not match the architectural decision date).
- **Two entries on the same date** — keep both, in the order they appear in CLAUDE.md (which is newest-first in `Recent Updates`).
- **Entry references `[[link]]` to other memories or files** — preserve verbatim. Do not resolve or rewrite links.
- **`Recent Updates` section is empty after rolling** — leave the heading intact; the next change adds entries back.
- **CHANGELOG.md does not exist yet** — create it with the canonical intro paragraph copied from the existing CHANGELOG.md schema (already exists at the path above, so this case is unlikely).
