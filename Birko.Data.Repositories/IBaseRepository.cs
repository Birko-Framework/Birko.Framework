using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    #region Delegate

    /// <summary>
    /// Delegate for processing repository data during operations.
    /// </summary>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public delegate TModel ProcessDataDelegate<TModel>(TModel data)
        where TModel : Models.AbstractModel;

    #endregion

    #region Base Repository Interface

    /// <summary>
    /// Defines the base lifecycle operations for repositories.
    /// </summary>
    public interface IBaseRepository
    {
        /// <summary>
        /// <b>PERMANENTLY DELETES every row the underlying store can see.</b> This is not disposal.
        /// </summary>
        /// <remarks>
        /// <para>
        /// SH-H046 (widened). This used to read <i>"Destroys the repository and releases all
        /// resources"</i>, matching the wording on <c>IBaseStore.Destroy()</c> -- and every repository
        /// family forwards straight to it (<c>AbstractRepository</c>, <c>AbstractAsyncRepository</c>,
        /// <c>AbstractViewModelRepository</c>, <c>AbstractAsyncViewModelRepository</c>). So this is not
        /// a milder operation one layer up; it is the same destruction, one call away.
        /// </para>
        /// <para>
        /// <b>The blast radius is the store's.</b> A SQL-backed repository reaches
        /// <c>DataBaseStore.Destroy()</c> -> <c>Connector.DropTable(...)</c> and the entity's table
        /// goes with every row in it; a RavenDB-backed one drops the <b>entire database</b>
        /// (<c>DeleteDatabasesOperation(hardDelete: true)</c>); CosmosDB deletes the container, MongoDB
        /// drops the collection, JSON and XML delete the file. Nothing is recoverable and nothing is
        /// scoped to a filter.
        /// </para>
        /// <para>
        /// No type in this contract declares <see cref="System.IDisposable"/>, so this was the only
        /// cleanup-looking member a caller could find here. To release resources, dispose the
        /// underlying store where it implements <c>IDisposable</c>; to delete selectively use the
        /// repository's filter-based delete; to empty a store while keeping it usable use
        /// <c>DeleteAll()</c> on <c>AbstractBulkStore&lt;T&gt;</c>.
        /// </para>
        /// </remarks>
        void Destroy();
    }

    #endregion

    #region Base Async Repository Interface

    /// <summary>
    /// Defines the base lifecycle operations for async repositories.
    /// </summary>
    public interface IAsyncBaseRepository
    {
        /// <summary>
        /// <b>PERMANENTLY DELETES every row the underlying store can see.</b> This is not disposal.
        /// </summary>
        /// <remarks>
        /// SH-H046 (widened). See <see cref="IBaseRepository.Destroy"/> for the full warning and the
        /// per-backend blast radius -- on RavenDB this drops the <b>entire database</b>, on SQL it
        /// drops the entity's table. This used to read <i>"asynchronously destroys the repository and
        /// releases all resources"</i>, which is disposal wording; every repository family forwards to
        /// the store's <c>DestroyAsync</c>, so it is the same destruction one call away. To release
        /// resources, dispose the underlying store where it implements <c>IDisposable</c>; to empty a
        /// store while keeping it usable use <c>DeleteAllAsync()</c> on
        /// <c>AbstractAsyncBulkStore&lt;T&gt;</c>.
        /// </remarks>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DestroyAsync(CancellationToken ct = default);
    }

    #endregion
}
