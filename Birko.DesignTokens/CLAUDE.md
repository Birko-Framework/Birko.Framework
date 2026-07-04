# Birko.DesignTokens — CLAUDE.md

Single-source design-token generator. `tokens.json` is the source of truth; a C# tool emits the
web CSS (byte-identical) and the Avalonia AXAML dictionaries. Part of **EPIC-015 (Birko.Xaml)**,
delivered by **STORY-029**.

See `README.md` for usage. This file records the conventions and non-obvious decisions.

## Scope

- **Owns:** `tokens.json` (the single source), the extractor/emitter, and the CLI (`extract` /
  `generate` / `verify`).
- **Writes into (does not own):**
  - `../../Web/Birko.Web.Components/css/{tokens.css, themes/*.css}` — the byte-identical parity gate.
  - `../Birko.Xaml.Avalonia/Themes/{Tokens.axaml, Theme.{Light,Dark,Neon,Finstat}.axaml}` — the
    `Birko.Xaml.Avalonia` project itself is created in **STORY-030**; STORY-029 only lays down the
    generated dictionaries in its `Themes/` folder.

## Convention deviations (deliberate — do not "fix")

1. **A real `.csproj`, not `.shproj`/`.projitems`.** It is the first buildable assembly in the
   `Birko\Framework` bucket. It is a build-time **tool** (needs an executable), not a runtime
   shared library. It is therefore **not** imported into `Birko.Framework.csproj` (the aggregator
   is for runtime shared projects only). Registered in `Birko.Framework.slnx` (new `/Xaml/`
   folder) and `.code-workspace`. Foreshadows EPIC-015's note that `Birko.Xaml.*` ship as real
   assemblies too.
2. **`tokens.json` is deliberately "dumb" / language-neutral.** No C#-specific constructs, no
   logic in the data. All formatting/emit knowledge lives in the generator code. This keeps the
   documented option of swapping the C# generator for a TypeScript one later cheap: rewrite one
   emitter against the committed CSS as a golden test — the schema and extracted data carry over
   untouched.

## Byte-identical parity — how it holds

- Model: `Sheet` = `prologue` + `{selector} {` + `Body[]` + `}` + `epilogue`. Each body line is
  one `Node`: `var` (`name`/`value`/`trail`) or `raw` (verbatim).
- **Values** are single-sourced on `var.value`. `trail` is the verbatim text after `;` (leading
  spaces + inline comment) — it preserves hand-authored comment-column alignment, which is *not*
  reconstructable from a rule (the source uses per-cluster visual alignment, not a global column).
  Do **not** try to derive alignment; keep it verbatim.
- **Line endings:** the repo is canonically **LF** (checked: `git show HEAD:` bytes are LF for all
  five files; `core.autocrlf=true` may present `tokens.css` as CRLF in a working tree). The model
  is kept LF; `CssIo.Normalize` folds CRLF→LF on read; `verify`/tests compare normalized. This
  makes generation machine-independent — do not switch to per-working-tree EOL.
- Gate: `CssIo.Emit(sheet)` must equal the normalized committed CSS. Enforced by
  `Birko.DesignTokens.Tests` and the `verify` verb.

## AXAML notes

- STORY-029 maps only the unambiguous, high-value tokens: colors (`Color`+`SolidColorBrush`),
  lengths (rem→px baked at 16), fonts (`FontFamily`), simple numerics. `var()` refs resolve
  per-theme so each theme dictionary is full & self-contained.
- Composite/motion tokens (shadows, focus rings, transitions, easings, durations, gradients) and
  the `ThemeVariant`/`DynamicResource` wiring are **STORY-030**. The `inverse` CSS theme (a scoped
  partial) is intentionally **not** emitted to AXAML yet.
- XML comments must not contain `--`; keep CLI flags out of generated comment text.

## Editing tokens

Edit a token's `value` in `tokens.json`, then `dotnet run --project Birko.DesignTokens -- generate`.
Never hand-edit the generated CSS or AXAML. Adding a theme = add a `Sheet`; adding a token = add a
`var` node to each relevant sheet. Re-run `generate`; the tests guard the CSS parity gate.

## Tests

`Birko.DesignTokens.Tests` (xUnit + FluentAssertions, in `Birko\Framework.Tests`): CSS round-trip
parity per sheet, extractor round-trip on the live files, single-source/uniqueness checks, AXAML
well-formedness, per-theme resolution, color/length/key-name conversion unit tests, and
cross-theme key-set parity (swap safety).
