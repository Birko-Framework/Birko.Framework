using Birko.Data.Stores;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// An async SQL bulk store seen without its connector type (TASK-988): CRUD, bulk, aggregation and its connector,
    /// whichever SQL provider built it. <see cref="AsyncDataBaseBulkStore{DB, T}"/> implements it, so every provider's
    /// async store is one. <see cref="ISqlStoreFactory"/> hands stores out as this.
    /// </summary>
    public interface IAsyncSqlBulkStore<T>
        : IAsyncBulkStore<T>
        , IAsyncAggregatableStore<T>
        , ISqlConnectedStore
        where T : Models.AbstractModel
    {
    }
}
