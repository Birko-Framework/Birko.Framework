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
        // SH-M374 (TASK-156): materialised once, so the entities validated are the entities written; a lazy sequence was
        // enumerated twice (a single-pass one was validated, then written empty).
        var items = data.ToList();
        await ValidateBatchAndThrowAsync(items, ct);
        await _innerStore.CreateAsync(items, storeDelegate, ct);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data.ToList(); // SH-M374
        await ValidateBatchAndThrowAsync(items, ct);
        await _innerStore.UpdateAsync(items, storeDelegate, ct);
    }

    /// <remarks>
    /// SH-M373 (TASK-156): validated. It forwarded unvalidated, so switching from UpdateAsync(entities) to the filtered
    /// form lost validation silently. The matches are read, changed, validated, and written as one collection update (one
    /// transaction on SQL, TASK-629); a filter that names no rows is refused like the inner store's.
    /// </remarks>
    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        RequireFilter(filter);
        var items = (await _innerStore.ReadAsync(filter, null, null, null, ct)).ToList();
        foreach (var item in items)
        {
            updateAction(item);
        }
        await ValidateBatchAndThrowAsync(items, ct);
        if (items.Count > 0)
        {
            await _innerStore.UpdateAsync(items, null, ct);
        }
    }

    /// <remarks>
    /// SH-M373 (TASK-156): validated against a preview, then run natively. The matches are read and the update applied to
    /// those copies only to validate them; the write itself stays the inner store's native filtered update, so an
    /// <c>Increment</c> keeps its atomicity (TASK-498). A row changed between the preview and the write is validated as it
    /// was read.
    /// </remarks>
    public async Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        RequireFilter(filter);
        var preview = (await _innerStore.ReadAsync(filter, null, null, null, ct)).ToList();
        foreach (var item in preview)
        {
            updates.ApplyTo(item);
        }
        await ValidateBatchAndThrowAsync(preview, ct);
        await _innerStore.UpdateAsync(filter, updates, ct);
    }

    private static void RequireFilter(Expression<Func<T, bool>> filter)
    {
        if (filter is null)
            throw new ArgumentNullException(nameof(filter), $"A filtered update of {typeof(T).Name} needs a filter.");
        Birko.Data.Expressions.BoundedFilterGuard.Require(filter, "update", typeof(T).Name, "UpdateAllAsync(updateAction)");
    }

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
