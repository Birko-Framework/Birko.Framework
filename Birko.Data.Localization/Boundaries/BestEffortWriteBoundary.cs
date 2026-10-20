using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Boundaries;

/// <summary>
/// The boundary used when none is given: the write runs as it is. A failure after the entity write leaves the entity
/// written; a failed translation cleanup after a delete is reported as <see cref="TranslationCleanupException"/>.
/// </summary>
/// <remarks>
/// Still atomic inside a caller's own transaction when the stores take part in it, as SQL stores on one connection do
/// in a caller's <c>SqlUnitOfWork</c>. This boundary does not know that, so it reports <see cref="IsAtomic"/> false.
/// </remarks>
public sealed class BestEffortWriteBoundary : ILocalizedWriteBoundary
{
    public static BestEffortWriteBoundary Instance { get; } = new();

    private BestEffortWriteBoundary()
    {
    }

    public bool IsAtomic => false;

    public void Run(Action write) => write();

    public Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct) => write(ct);
}
