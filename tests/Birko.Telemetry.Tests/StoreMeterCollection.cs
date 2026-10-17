using Xunit;

namespace Birko.Telemetry.Tests;

/// <summary>
/// TASK-657 (FIELD-048): every class that publishes to or listens on the process-wide <c>Birko.Data.Store</c> meter runs
/// in this collection, so one class's measurements never arrive in another's listener mid-test. Every
/// <see cref="System.Diagnostics.Metrics.MeterListener"/> receives every measurement of the meter, whichever thread made it.
/// Measured: a listener appending to a plain <c>List</c> while siblings published lost 3-4% of its entries, with no
/// exception, and CI run 39054139288 failed on exactly that: six sibling <c>count</c> entries and the test's own
/// <c>errors</c> entry missing (rule 61's shape: the victim is not the file that caused it).
/// </summary>
[CollectionDefinition(Name)]
public sealed class StoreMeterCollection
{
    public const string Name = "Birko.Data.Store meter";
}
