using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;

namespace Birko.Data.Stores
{
    /// <summary>
    /// The one producer of <c>Save</c>'s create-or-update decision (TASK-555). It used to be re-implemented in ~30
    /// <c>Save</c> bodies — base stores, repositories and every decorator — each routing a non-empty Guid to
    /// <c>Update</c>, which wrote nothing when the entity was not stored and still returned the Guid (SH-M309).
    /// </summary>
    /// <remarks>
    /// <para>A null or empty Guid creates. Any other Guid updates, and when the update reports
    /// <see cref="EntityNotFoundException"/> for that Guid the entity is created under it — an upsert that reads
    /// each provider's own matched count instead of a pre-read. A caller passes its OWN create / update so its
    /// decoration (timestamps, audit, tenant, validation…) runs on whichever path is taken.</para>
    /// <para>The store delegate is wrapped to run once: an update that invoked it and then missed falls through to
    /// create, which must not apply it a second time.</para>
    /// </remarks>
    public static class StoreSave
    {
        public static Guid Save<T>(
            T? data,
            StoreDataDelegate<T>? storeDelegate,
            Func<T, StoreDataDelegate<T>?, Guid> create,
            Action<T, StoreDataDelegate<T>?> update)
            where T : AbstractModel
        {
            if (data == null)
            {
                return Guid.Empty;
            }
            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                return create(data, storeDelegate);
            }

            var once = RunOnce(storeDelegate);
            var guid = data.Guid.Value;
            try
            {
                update(data, once);
                return guid;
            }
            catch (EntityNotFoundException ex) when (ex.Guids.Contains(guid))
            {
                return create(data, once);
            }
        }

        public static async Task<Guid> SaveAsync<T>(
            T? data,
            StoreDataDelegate<T>? storeDelegate,
            Func<T, StoreDataDelegate<T>?, CancellationToken, Task<Guid>> create,
            Func<T, StoreDataDelegate<T>?, CancellationToken, Task> update,
            CancellationToken ct = default)
            where T : AbstractModel
        {
            if (data == null)
            {
                return Guid.Empty;
            }
            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                return await create(data, storeDelegate, ct).ConfigureAwait(false);
            }

            var once = RunOnce(storeDelegate);
            var guid = data.Guid.Value;
            try
            {
                await update(data, once, ct).ConfigureAwait(false);
                return guid;
            }
            catch (EntityNotFoundException ex) when (ex.Guids.Contains(guid))
            {
                return await create(data, once, ct).ConfigureAwait(false);
            }
        }

        private static StoreDataDelegate<T>? RunOnce<T>(StoreDataDelegate<T>? storeDelegate) where T : AbstractModel
        {
            if (storeDelegate == null)
            {
                return null;
            }
            var ran = 0;
            return data => Interlocked.Exchange(ref ran, 1) == 0 ? storeDelegate(data) : data;
        }
    }
}
