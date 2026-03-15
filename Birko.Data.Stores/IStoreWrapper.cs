using System;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Interface for store wrappers that wrap an inner store.
    /// Implementations include tenant wrappers, event sourcing wrappers, etc.
    /// </summary>
    public interface IStoreWrapper
    {
        /// <summary>
        /// Gets the inner wrapped store.
        /// </summary>
        object? GetInnerStore();
    }

    /// <summary>
    /// Generic version of IStoreWrapper for strongly-typed access.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface IStoreWrapper<out T> : IStoreWrapper
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the inner wrapped store as the specified type.
        /// </summary>
        /// <typeparam name="TStore">The type of store to cast to.</typeparam>
        /// <returns>The inner store if it matches the type, otherwise null.</returns>
        TStore? GetInnerStoreAs<TStore>() where TStore : class;
    }
}
