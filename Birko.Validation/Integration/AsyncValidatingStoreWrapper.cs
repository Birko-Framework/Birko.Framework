using Birko.Data.Stores;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Validation.Integration;

/// <summary>
/// Async store wrapper that validates entities before Create/Update.
/// Throws <see cref="ValidationException"/> on validation failure.
/// </summary>
public class AsyncValidatingStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel
{
    protected readonly TStore _innerStore;
    protected readonly IValidator<T> _validator;

    public AsyncValidatingStoreWrapper(TStore innerStore, IValidator<T> validator)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _innerStore.ReadAsync(guid, ct);
    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _innerStore.ReadAsync(filter, ct);
    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _innerStore.CountAsync(filter, ct);

    public async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await ValidateAndThrowAsync(data, ct);
        return await _innerStore.CreateAsync(data, processDelegate, ct);
    }

    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await ValidateAndThrowAsync(data, ct);
        await _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    public Task DeleteAsync(T data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await ValidateAndThrowAsync(data, ct);
        return await _innerStore.SaveAsync(data, processDelegate, ct);
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    protected async Task ValidateAndThrowAsync(T data, CancellationToken ct)
    {
        var result = await _validator.ValidateAsync(data, ct);
        if (!result.IsValid)
            throw new ValidationException(result);
    }

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
