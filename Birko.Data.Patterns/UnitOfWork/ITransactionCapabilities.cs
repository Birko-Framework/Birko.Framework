namespace Birko.Data.Patterns.UnitOfWork;

/// <summary>
/// How strongly a backend's boundary holds.
/// </summary>
public enum TransactionAtomicity
{
    /// <summary>No boundary at all. Operations are independent and none can be undone.</summary>
    None = 0,

    /// <summary>
    /// Operations are batched and sent together, but individual operations may succeed or fail
    /// independently — a partial application is possible and is not automatically undone.
    /// </summary>
    BestEffort = 1,

    /// <summary>All-or-nothing within <see cref="ITransactionCapabilities.Scope"/>.</summary>
    Atomic = 2,
}

/// <summary>
/// How wide a boundary can be drawn on a backend.
/// </summary>
public enum TransactionBoundaryScope
{
    /// <summary>No boundary can be drawn.</summary>
    None = 0,

    /// <summary>One document only. Anything spanning two documents is outside the boundary.</summary>
    SingleDocument = 1,

    /// <summary>
    /// One logical partition key. A boundary spanning two partitions cannot exist, whatever the API
    /// lets a caller type.
    /// </summary>
    SinglePartition = 2,

    /// <summary>Any number of tables/collections within one database.</summary>
    Database = 3,

    /// <summary>Spans nodes of a cluster.</summary>
    Cluster = 4,
}

/// <summary>
/// What a backend's transaction boundary actually promises.
/// </summary>
/// <remarks>
/// Exists because the backends genuinely differ and a contract that hid the difference would be worse
/// than none: MongoDB needs a replica set, Cosmos cannot span two partition keys, and ElasticSearch has
/// no transaction concept at all. Modelled on <c>IJobLockProvider.IsLeaseBased</c> — surface the
/// distinction rather than smoothing it over, so a caller cannot believe it has cover it does not.
/// </remarks>
public interface ITransactionCapabilities
{
    /// <summary>How strongly the boundary holds.</summary>
    TransactionAtomicity Atomicity { get; }

    /// <summary>How wide the boundary can be drawn.</summary>
    TransactionBoundaryScope Scope { get; }

    /// <summary>
    /// Whether a read issued inside the boundary sees the boundary's own uncommitted writes.
    /// </summary>
    /// <remarks>
    /// False means read-then-write logic inside the boundary reads a stale snapshot. That is a wrong
    /// answer rather than a missing feature, so it is stated explicitly rather than left to be
    /// discovered. Cosmos is false by construction: a <c>TransactionalBatch</c> buffers writes
    /// client-side until execute and exposes no read.
    /// </remarks>
    bool ReadsSeeUncommittedWrites { get; }

    /// <summary>
    /// Whether the server must be deployed in a particular topology for the boundary to work at all.
    /// </summary>
    /// <remarks>
    /// True for MongoDB, whose multi-document transactions require a replica set or sharded cluster —
    /// against a standalone <c>mongod</c> they fail at runtime, not at startup.
    /// </remarks>
    bool RequiresServerTopology { get; }

    /// <summary>Human-readable statement of what this backend cannot promise. Null when unrestricted.</summary>
    string? Limitations { get; }
}

/// <summary>
/// Plain immutable <see cref="ITransactionCapabilities"/>.
/// </summary>
public sealed class TransactionCapabilities : ITransactionCapabilities
{
    public TransactionCapabilities(
        TransactionAtomicity atomicity,
        TransactionBoundaryScope scope,
        bool readsSeeUncommittedWrites,
        bool requiresServerTopology = false,
        string? limitations = null)
    {
        Atomicity = atomicity;
        Scope = scope;
        ReadsSeeUncommittedWrites = readsSeeUncommittedWrites;
        RequiresServerTopology = requiresServerTopology;
        Limitations = limitations;
    }

    public TransactionAtomicity Atomicity { get; }
    public TransactionBoundaryScope Scope { get; }
    public bool ReadsSeeUncommittedWrites { get; }
    public bool RequiresServerTopology { get; }
    public string? Limitations { get; }

    /// <summary>
    /// The answer for a backend that has no transaction concept at all.
    /// </summary>
    public static readonly TransactionCapabilities None = new(
        TransactionAtomicity.None,
        TransactionBoundaryScope.None,
        readsSeeUncommittedWrites: false,
        limitations: "This backend has no transaction concept. Operations cannot be rolled back.");

    public override string ToString()
        => $"{Atomicity} within {Scope}"
         + (ReadsSeeUncommittedWrites ? ", reads see own writes" : ", reads do NOT see own writes")
         + (RequiresServerTopology ? ", requires a specific server topology" : string.Empty);
}
