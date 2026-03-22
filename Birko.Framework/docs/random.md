# Birko.Random — Random Number Generators

## Overview

Pluggable random number generation with testable abstractions. Zero dependencies.

## Architecture

```
IRandomProvider (abstraction)
  ├── SystemRandomProvider    (System.Random.Shared, thread-safe)
  ├── CryptoRandomProvider    (RandomNumberGenerator, thread-safe, crypto-secure)
  ├── XorShiftProvider        (XorShift128, fast, seeded)
  ├── MersenneTwisterProvider (MT19937, high quality, seeded)
  ├── SplitMixProvider        (SplitMix64, very fast, seeded)
  └── TestRandomProvider      (queued values, deterministic)

Distributions (take IRandomProvider)
  ├── UniformDistribution
  ├── NormalDistribution      (Box-Muller)
  ├── ExponentialDistribution
  ├── PoissonDistribution     (Knuth + rejection)
  └── BernoulliDistribution

Sequences (static, crypto-secure)
  ├── GuidGenerator           (v4 random, v7 time-ordered)
  ├── NanoIdGenerator         (URL-safe compact IDs)
  ├── SnowflakeGenerator      (64-bit time-ordered, thread-safe)
  └── TokenGenerator          (hex, Base64URL, API keys)

Noise (seeded, deterministic)
  ├── PerlinNoise             (1D/2D/3D + fBm)
  └── SimplexNoise            (2D/3D + fBm)
```

## Provider Selection Guide

| Scenario | Provider |
|----------|----------|
| General purpose | `SystemRandomProvider` |
| Security tokens, keys | `CryptoRandomProvider` |
| Load testing, simulations | `XorShiftProvider` or `SplitMixProvider` |
| Statistical sampling | `MersenneTwisterProvider` |
| Unit tests | `TestRandomProvider` |

## Distribution Use Cases

| Distribution | Use Case | Parameters |
|-------------|----------|------------|
| Uniform | Even spread, dice rolls | min, max |
| Normal | Bell curve, load testing | mean, stdDev |
| Exponential | Retry jitter, inter-arrival times | rate (lambda) |
| Poisson | Request counts per interval | lambda |
| Bernoulli | Coin flips, A/B testing, feature flags | probability |

## Sequence Formats

| Generator | Format | Sortable | Crypto | Use Case |
|-----------|--------|----------|--------|----------|
| GuidV4 | 128-bit UUID | No | Yes | Random unique IDs |
| GuidV7 | 128-bit UUID | Yes (time) | Partial | DB primary keys |
| NanoId | 21-char string | No | Yes | URL-safe compact IDs |
| Snowflake | 64-bit long | Yes (time) | No | Distributed systems |
| Token (hex) | Hex string | No | Yes | API keys |
| Token (Base64URL) | Base64URL string | No | Yes | Reset tokens |

## Testing

Inject `TestRandomProvider` to control randomness in tests:

```csharp
var rng = new TestRandomProvider();
rng.EnqueueDouble(0.1, 0.5, 0.9);

var dist = new NormalDistribution(rng, mean: 100, stdDev: 10);
// Distribution will use queued doubles for Box-Muller transform
```

## Thread Safety

- **Thread-safe:** `SystemRandomProvider`, `CryptoRandomProvider`, `SnowflakeGenerator`
- **Not thread-safe:** `XorShiftProvider`, `MersenneTwisterProvider`, `SplitMixProvider`, `TestRandomProvider`
- **Static methods (thread-safe):** `GuidGenerator`, `NanoIdGenerator`, `TokenGenerator`
