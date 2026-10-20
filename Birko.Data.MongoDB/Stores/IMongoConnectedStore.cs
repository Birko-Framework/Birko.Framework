using MongoDB.Driver;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// A MongoDB store's client and transaction context without its model type (TASK-700): how composing code tells two
    /// stores share one database (<see cref="AmbientKey"/>) and whether a caller gave one a session of its own.
    /// </summary>
    public interface IMongoConnectedStore
    {
        /// <summary>The store's client; null until <c>SetSettings</c>.</summary>
        MongoDBClient? Client { get; }

        /// <summary>The session a caller gave this store directly, if any.</summary>
        IClientSessionHandle? TransactionContext { get; }

        /// <summary>The key a session for this store's database is published under; null until <c>SetSettings</c>.</summary>
        string? AmbientKey { get; }
    }
}
