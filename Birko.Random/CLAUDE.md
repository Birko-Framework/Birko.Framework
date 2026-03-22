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
- `PerlinNoise` — Classic Perlin noise (1D/2D/3D + fBm)
- `SimplexNoise` — Simplex noise (2D/3D + fBm), fewer artifacts than Perlin

## Dependencies
None. This is a zero-dependency project.

## Maintenance
- These types must remain zero-dependency
- Providers that are not thread-safe must document this in their XML doc
- All distributions take `IRandomProvider` for testability
- Cryptographic operations (TokenGenerator, GuidGenerator, CryptoRandomProvider) use `System.Security.Cryptography.RandomNumberGenerator`
