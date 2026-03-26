using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Validation.Integration;

/// <summary>
/// Async bulk store wrapper that validates entities before Create/Update.
/// Throws <see cref="ValidationException"/> with all errors aggregated.
/// </summary>
public class AsyncValidatingBulkStoreWrapper<TStore, T> : AsyncValidatingStoreWrapper<TStore, T>, IAsyncBulkStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
{
    public AsyncValidatingBulkStoreWrapper(TStore innerStore, IValidator<T> validator) : base(innerStore, validator) { }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _innerStore.ReadAsync(ct);

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(filter, orderBy, limit, offset, ct);
    }

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        await ValidateBatchAndThrowAsync(data, ct);
        await _innerStore.CreateAsync(data, storeDelegate, ct);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        await ValidateBatchAndThrowAsync(data, ct);
        await _innerStore.UpdateAsync(data, storeDelegate, ct);
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        await _innerStore.UpdateAsync(filter, updateAction, ct);
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default) => _innerStore.UpdateAsync(filter, updates, ct);

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);

    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => _innerStore.DeleteAsync(filter, ct);

    private async Task ValidateBatchAndThrowAsync(IEnumerable<T> data, CancellationToken ct)
    {
        var aggregated = new ValidationResult();
        foreach (var item in data)
        {
            var result = await _validator.ValidateAsync(item, ct);
            if (!result.IsValid)
                aggregated.Merge(result);
        }

        if (!aggregated.IsValid)
            throw new ValidationException(aggregated);
    }
}
