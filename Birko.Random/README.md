# Birko.Random

Pluggable random number generation with testable abstractions for the Birko Framework.

## Overview

Birko.Random provides a unified `IRandomProvider` interface with multiple implementations, statistical distributions, ID/token generators, and noise functions — all with zero external dependencies.

## Providers

| Provider | Thread-Safe | Use Case |
|----------|-------------|----------|
| `SystemRandomProvider` | Yes | General-purpose (uses `Random.Shared`) |
| `CryptoRandomProvider` | Yes | Secure tokens, keys, security-sensitive |
| `XorShiftProvider` | No | Fast simulations, non-crypto |
| `MersenneTwisterProvider` | No | Statistical sampling, simulations |
| `SplitMixProvider` | No | Very fast PRNG, seeding other PRNGs |
| `TestRandomProvider` | No | Unit tests with queued/deterministic values |

## Distributions

| Distribution | Parameters | Use Case |
|-------------|------------|----------|
| `UniformDistribution` | min, max | Even spread |
| `NormalDistribution` | mean, stdDev | Bell curve, load testing |
| `ExponentialDistribution` | rate (lambda) | Retry jitter, inter-arrival times |
| `PoissonDistribution` | lambda | Event counts per interval |
| `BernoulliDistribution` | probability | Coin flips, A/B testing |

## Sequences

| Generator | Output | Use Case |
|-----------|--------|----------|
| `GuidGenerator.NewGuidV4()` | UUID v4 | Random unique IDs |
| `GuidGenerator.NewGuidV7()` | UUID v7 | Time-sortable DB primary keys |
| `NanoIdGenerator.New()` | 21-char string | URL-safe compact IDs |
| `SnowflakeGenerator.Next()` | 64-bit long | Distributed time-ordered IDs |
| `TokenGenerator.NewHex()` | Hex string | API keys |
| `TokenGenerator.NewBase64Url()` | Base64URL string | Reset tokens, sessions |
| `TokenGenerator.NewApiKey("sk_live")` | Prefixed token | Prefixed API keys |

## Noise

| Generator | Dimensions | Use Case |
|-----------|-----------|----------|
| `PerlinNoise` | 1D, 2D, 3D + fBm | Procedural content, test data |
| `SimplexNoise` | 2D, 3D + fBm | Fewer artifacts than Perlin |

## Dependencies

None. This is a zero-dependency project.

## Usage

```xml
<Import Project="..\Birko.Random\Birko.Random.projitems" Label="Shared" />
```

```csharp
using Birko.Random;

// Production
IRandomProvider rng = new SystemRandomProvider();
var value = rng.NextDouble();

// Distributions
var normal = new NormalDistribution(rng, mean: 100, stdDev: 15);
var sample = normal.Next();

// IDs
var id = GuidGenerator.NewGuidV7();
var nanoId = NanoIdGenerator.New();
var token = TokenGenerator.NewApiKey("sk_live");

// Noise
var perlin = new PerlinNoise(seed: 42);
var noise = perlin.Fbm(x: 1.5, y: 2.3);

// Testing
var testRng = new TestRandomProvider();
testRng.EnqueueDouble(0.1, 0.5, 0.9);
```

## License

MIT License - see [License.md](License.md)
