using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Boundaries;

/// <summary>
/// How a write that touches both the entity store and the translation store is made one unit (TASK-683). The localized
/// wrappers and <c>IEntityTranslations</c> run every such write through it.
/// </summary>
/// <remarks>
/// This project stays free of any storage technology, so it only declares the seam. <see cref="BestEffortWriteBoundary"/>
/// runs the write as it is. <c>Birko.Data.Localization.SQL</c>'s <c>SqlLocalizedWriteBoundary.For</c> runs it in one
/// transaction when both stores are on one SQL connection.
/// </remarks>
public interface ILocalizedWriteBoundary
{
    /// <summary>
    /// True when a write that throws leaves neither store changed. False when the half already written stays, which is
    /// best effort.
    /// </summary>
    bool IsAtomic { get; }

    /// <summary>Runs <paramref name="write"/> as one unit, as far as <see cref="IsAtomic"/> promises.</summary>
    void Run(Action write);

    /// <inheritdoc cref="Run"/>
    Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct);
}
