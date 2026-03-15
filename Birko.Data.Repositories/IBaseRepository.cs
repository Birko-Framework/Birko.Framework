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
        /// Destroys the repository and releases all resources.
        /// </summary>
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
        /// Asynchronously destroys the repository and releases all resources.
        /// </summary>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        Task DestroyAsync(CancellationToken ct = default);
    }

    #endregion
}
