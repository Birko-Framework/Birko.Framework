using System;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that supports batch tracking with optional expiry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both members are optional, and that is the contract.</b> Batch tracking is a per-item choice in
    /// every real inventory — most stock carries no batch — and a batch that exists may still not expire
    /// (a serial number, a paint lot). An entity implements this to say "batch tracking is *available*
    /// here", not "every row has one".
    /// </para>
    /// <para>
    /// <c>BatchNumber</c> was originally declared non-nullable <c>string</c>, which made the contract
    /// unimplementable by exactly the entities it was written for and is the most likely reason it had
    /// <b>zero implementors</b> for its whole life (TASK-444). Changed to <c>string?</c> while there were
    /// still none to break.
    /// </para>
    /// </remarks>
    public interface IBatchable
    {
        /// <summary>Batch / lot identifier, or <see langword="null"/> when this row is not batch-tracked.</summary>
        string? BatchNumber { get; set; }

        /// <summary>Expiry of the batch, or <see langword="null"/> when it does not expire.</summary>
        DateTime? ExpiryDate { get; set; }
    }
}
