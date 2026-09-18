# Birko.DesignTokens.Tests

xUnit + FluentAssertions tests for [`Birko.DesignTokens`](../Birko.DesignTokens).

## Coverage

- **`CssParityTests`** — regenerating each sheet from `tokens.json` is byte-identical to
  the committed hand-authored CSS (LF-normalized); all five sheets present
  (light/dark/neon/finstat/inverse); every sheet has a selector and non-empty body; each
  token declared once per sheet; and `CssIo.Extract` → `CssIo.Emit` round-trips the live
  CSS losslessly.
- **`AxamlEmitterTests`** — `AxamlEmitter.Generate` emits a single well-formed
  `Tokens.axaml` with one `ThemeDictionaries` entry per variant, custom variants keyed by
  `{x:Static themes:BirkoThemeVariants.*}`, per-variant colour/`var()` resolution, a shared
  key set across all variants, and a `DynamicResource`-linked root brush per colour. Plus
  conversion unit tests for `TryColor`, `TryLengthToPx`, and `ToKey`.

## Test framework

- xUnit
- FluentAssertions

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
