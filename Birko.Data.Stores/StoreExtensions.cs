using System;
using System.Linq;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Extension methods for working with stores and wrappers.
    /// </summary>
    public static class StoreExtensions
    {
        /// <summary>
        /// Recursively unwraps a store to get the innermost non-wrapped store.
        /// </summary>
        /// <param name="store">The store to unwrap.</param>
        /// <returns>The innermost non-wrapped store, or the original store if it's not a wrapper.</returns>
        public static object? GetUnwrappedStore(this object? store)
        {
            if (store == null)
            {
                return null;
            }

            var current = store;
            while (current is IStoreWrapper wrapper)
            {
                var inner = wrapper.GetInnerStore();
                if (inner == null)
                {
                    break;
                }
                current = inner;
            }
            return current;
        }

        /// <summary>
        /// Recursively unwraps a store to get the innermost non-wrapped store of a specific type.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <typeparam name="TStore">The expected store type.</typeparam>
        /// <param name="store">The store to unwrap.</param>
        /// <returns>The innermost store of the specified type, or null if not found.</returns>
        public static TStore? GetUnwrappedStore<T, TStore>(this IStore<T>? store)
            where T : Models.AbstractModel
            where TStore : class
        {
            if (store == null)
            {
                return null;
            }

            var unwrapped = GetUnwrappedStore((object)store);
            return unwrapped as TStore;
        }

        /// <summary>
        /// Checks if a store (or its inner wrapped store) is of the expected type.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <typeparam name="TStore">The expected store type.</typeparam>
        /// <param name="store">The store to check.</param>
        /// <returns>True if the store or its inner store is of the expected type.</returns>
        public static bool IsStoreOfType<T, TStore>(this IStore<T>? store)
            where T : Models.AbstractModel
            where TStore : class
        {
            if (store == null)
            {
                return false;
            }

            return IsOrWraps<TStore>(store);
        }

        /// <summary>
        /// Recursively unwraps an async store to get the innermost non-wrapped store of a specific type.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <typeparam name="TStore">The expected store type.</typeparam>
        /// <param name="store">The async store to unwrap.</param>
        /// <returns>The innermost store of the specified type, or null if not found.</returns>
        public static TStore? GetUnwrappedStore<T, TStore>(this IAsyncStore<T>? store)
            where T : Models.AbstractModel
            where TStore : class
        {
            if (store == null)
            {
                return null;
            }

            var unwrapped = GetUnwrappedStore((object)store);
            return unwrapped as TStore;
        }

        /// <summary>
        /// Checks if an async store (or its inner wrapped store) is of the expected type.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <typeparam name="TStore">The expected store type.</typeparam>
        /// <param name="store">The async store to check.</param>
        /// <returns>True if the store or its inner store is of the expected type.</returns>
        public static bool IsStoreOfType<T, TStore>(this IAsyncStore<T>? store)
            where T : Models.AbstractModel
            where TStore : class
        {
            if (store == null)
            {
                return false;
            }

            return IsOrWraps<TStore>(store);
        }

        /// <summary>
        /// Checks if a bulk store (or its inner wrapped store) is of the expected type.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <typeparam name="TStore">The expected store type.</typeparam>
        /// <param name="store">The bulk store to check.</param>
        /// <returns>True if the store or its inner store is of the expected type.</returns>
        public static bool IsStoreOfType<T, TStore>(this IBulkStore<T>? store)
            where T : Models.AbstractModel
            where TStore : class
        {
            if (store == null)
            {
                return false;
            }

            return IsOrWraps<TStore>(store);
        }

        /// <summary>
        /// Checks if an async bulk store (or its inner wrapped store) is of the expected type.
        /// </summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <typeparam name="TStore">The expected store type.</typeparam>
        /// <param name="store">The async bulk store to check.</param>
        /// <returns>True if the store or its inner store is of the expected type.</returns>
        public static bool IsStoreOfType<T, TStore>(this IAsyncBulkStore<T>? store)
            where T : Models.AbstractModel
            where TStore : class
        {
            if (store == null)
            {
                return false;
            }

            return IsOrWraps<TStore>(store);
        }

        /// <summary>
        /// The store itself, or the innermost store of its wrapper chain, is a <typeparamref name="TStore"/> — the
        /// store a backend repository then reaches through <see cref="GetUnwrappedStore(object?)"/>.
        /// </summary>
        /// <remarks>
        /// SH-M302: this used to ask the first wrapper for its <i>immediate</i> inner store
        /// (<see cref="IStoreWrapper{T}.GetInnerStoreAs{TStore}"/>, one level by contract) and return that answer,
        /// so on a chain of two wrappers — what <c>StoreWrapperBuilder</c> builds — the inner was another wrapper
        /// and every backend repository's constructor rejected a correctly composed store.
        /// </remarks>
        private static bool IsOrWraps<TStore>(object store)
            where TStore : class
        {
            return store is TStore || GetUnwrappedStore(store) is TStore;
        }
    }
}
