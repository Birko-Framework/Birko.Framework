using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// A SQL store's connector, reachable without knowing the store's generic arguments. Code that composes stores it
    /// did not create uses it to tell whether two stores share one database, by comparing
    /// <c>Connector.Settings.GetId()</c>: the key the ambient transaction is looked up by (TASK-683).
    /// </summary>
    public interface ISqlConnectedStore
    {
        /// <summary>The store's connector; <c>null</c> until its settings are set.</summary>
        AbstractConnector? Connector { get; }

        /// <summary>The transaction given with <c>SetTransactionContext</c>, if any.</summary>
        SqlTransactionContext? TransactionContext { get; }
    }
}
