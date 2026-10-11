using System;
using Birko.Data.Models;

namespace Birko.Data.Stores
{
    /// <summary>
    /// The one definition of what a store does with a <see cref="StoreDataDelegate{T}"/> (SH-M315, TASK-557).
    /// </summary>
    /// <remarks>
    /// <para>The delegate returns <typeparamref name="T"/>, but every call site used to discard the result and persist
    /// the object it passed in — so a delegate written in the functional style (return a modified copy) was silently
    /// ignored. A store now persists what the delegate returns; a <c>null</c> return keeps the original.</para>
    /// <para>Identity is assigned and checked before the delegate runs, so a replacement that carries no Guid of its
    /// own inherits the original's — otherwise an update would address a different entity and a create would mint a
    /// second id. A replacement that sets its own Guid keeps it.</para>
    /// </remarks>
    public static class StoreDelegates
    {
        /// <summary>Runs <paramref name="storeDelegate"/> on <paramref name="data"/> and returns what to persist.</summary>
        public static T Apply<T>(StoreDataDelegate<T>? storeDelegate, T data) where T : AbstractModel
        {
            if (storeDelegate == null)
            {
                return data;
            }
            var result = storeDelegate(data) ?? data;
            if (!ReferenceEquals(result, data) && (result.Guid == null || result.Guid == Guid.Empty))
            {
                result.Guid = data.Guid;
            }
            return result;
        }
    }
}
