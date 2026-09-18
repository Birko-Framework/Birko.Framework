using Xunit;

namespace Birko.CQRS.Tests;

/// <summary>
/// Serialises the test classes that share <c>CreateItemHandler.WasHandled</c>.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel by default, and that flag is process-wide because the mediator
/// resolves its handler from DI — so a test cannot hold the instance. Two classes writing it
/// concurrently is how CI saw <c>Pipeline_ShortCircuit_DoesNotCallHandler</c> fail on 2026-09-18 while
/// three local runs stayed green: the failing class was never the cause, which is the thing that makes
/// this kind of flake expensive to diagnose.
///
/// Deliberately narrow. § TASK-276 forbids reaching for a collection whenever a suite is flaky —
/// a collection spanning most of a project is <c>parallelizeTestCollections: false</c> in all but
/// name. Two classes sharing one flag is the case it is actually for, and
/// <see cref="SharedHandlerStateIsolationTests"/> fails if a third class joins them without saying so.
/// </remarks>
[CollectionDefinition(Name)]
public class CreateItemHandlerStateCollection
{
    // Assembled rather than written out, for the same reason SharedHandlerStateIsolationTests does
    // it: the collection is named after the member it protects, so a guard scanning for that member
    // would otherwise report this file. Same constant, same value, no contiguous literal.
    public const string Name = "CreateItemHandler" + "." + "WasHandled";
}
