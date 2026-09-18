# Birko.Random

## Overview
Zero-dependency shared project providing pluggable random number generation with testable abstractions. Includes providers, statistical distributions, ID/token sequences, and noise generators.

## Project Location
`C:\Source\Birko.Random\`

## Namespace
`Birko.Random`

## Components

### Core/IRandomProvider.cs
- `IRandomProvider` — Abstraction over random number generation: `NextInt`, `NextLong`, `NextDouble`, `NextBytes`, `NextBool`

### Providers/
- `SystemRandomProvider` — Production implementation using `System.Random.Shared` (thread-safe)
- `CryptoRandomProvider` — Cryptographically secure using `RandomNumberGenerator` (thread-safe)
- `XorShiftProvider` — XorShift128 PRNG, fast for simulations (not thread-safe)
- `MersenneTwisterProvider` — MT19937, high-quality with 2^19937-1 period (not thread-safe)
- `SplitMixProvider` — SplitMix64, very fast 64-bit PRNG (not thread-safe)
- `TestRandomProvider` — Test-controllable with queued return values and defaults

### Distributions/
- `UniformDistribution` — Uniform values in [min, max)
- `NormalDistribution` — Gaussian via Box-Muller transform
- `ExponentialDistribution` — Exponential for inter-arrival times, retry jitter
- `PoissonDistribution` — Poisson for event counts (Knuth + rejection method)
- `BernoulliDistribution` — Boolean/coin-flip with configurable probability

### Sequences/
- `GuidGenerator` — UUID v4 (random) and v7 (time-ordered, RFC 9562)
- `NanoIdGenerator` — URL-safe compact IDs (configurable alphabet/size)
- `SnowflakeGenerator` — Twitter Snowflake 64-bit time-ordered IDs (thread-safe)
- `TokenGenerator` — Cryptographic tokens (hex, Base64URL, URL-safe, prefixed API keys)

### Noise/
- `NoisePermutation` — internal; builds the seeded permutation table both generators share
- `PerlinNoise` — Classic Perlin noise (1D/2D/3D + fBm)
- `SimplexNoise` — Simplex noise (2D/3D + fBm), fewer artifacts than Perlin

## Reproducibility — which types are stable across runtimes

**Read this before picking a generator for replay, lockstep, procedural generation or golden-file
tests.** "Seedable" and "reproducible" are not the same property, and the difference is invisible at
the call site: a type that takes a seed *looks* like it promises a stable sequence.

| Type | Seedable | Same sequence on every runtime / platform |
|---|---|---|
| `SplitMixProvider(ulong)` | ✅ | ✅ pure 64-bit integer arithmetic defined here |
| `XorShiftProvider(ulong)` | ✅ | ✅ same, see the note below |
| `MersenneTwisterProvider(uint)` | ✅ | ✅ same |
| `PerlinNoise(int)` / `SimplexNoise(int)` | ✅ | ✅ since TASK-449 — seeded from `SplitMixProvider` |
| `TestRandomProvider` | n/a | ✅ returns what you queued |
| `SystemRandomProvider` | ❌ | ❌ **deliberately** — wraps `System.Random.Shared`, which is also shared across threads |
| `CryptoRandomProvider` | ❌ | ❌ **deliberately** — it would not be cryptographic otherwise |

`NextDouble()` on the integer providers is `(NextUInt64() >> 11) * (1.0 / (1UL << 53))` — an integer
shift multiplied by an exactly-representable power-of-two reciprocal, so IEEE 754 makes it
bit-identical everywhere. Anything derived from it (`NextInt(max)`, the distributions) inherits that.

⚠ **Never seed a reproducible sequence from `System.Random`.** Its algorithm changed in .NET 6 (the
subtractive generator was replaced with xoshiro256\*\*) and .NET guarantees no cross-version
stability for a seeded sequence. That is exactly what TASK-449 fixed in the noise generators, where
the same seed silently produced different output after a framework upgrade — no exception, no
warning, and the stored seed still looking like it was doing its job. A test in
`Birko.Random.Tests/Noise/NoiseReproducibilityTests.cs` scans the noise sources so it cannot come
back, and pins known vectors per seed.

⚠ **The distributions and sequences inherit the provider you give them** — `NormalDistribution`,
`ExponentialDistribution` and friends are reproducible exactly when their `IRandomProvider` is.
`GuidGenerator`, `TokenGenerator` and `SnowflakeGenerator` are **not** reproducible by design (v4/v7
UUIDs, cryptographic tokens, and wall-clock-derived ids respectively).

## Dependencies
None. This is a zero-dependency project.

## Maintenance
- These types must remain zero-dependency
- Providers that are not thread-safe must document this in their XML doc
- All distributions take `IRandomProvider` for testability
- Cryptographic operations (TokenGenerator, GuidGenerator, CryptoRandomProvider) use `System.Security.Cryptography.RandomNumberGenerator`
