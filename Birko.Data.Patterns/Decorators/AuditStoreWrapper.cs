using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that automatically sets CreatedBy/UpdatedBy from IAuditContext.
/// </summary>
public class AuditStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, IAuditable
{
    protected readonly TStore _innerStore;
    protected readonly IAuditContext _auditContext;

    public AuditStoreWrapper(TStore innerStore, IAuditContext auditContext)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public T? Read(Guid guid) => _innerStore.Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null) => _innerStore.Read(filter);
    public long Count(Expression<Func<T, bool>>? filter = null) => _innerStore.Count(filter);

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        data.CreatedBy = _auditContext.CurrentUserId;
        data.UpdatedBy = _auditContext.CurrentUserId;
        return _innerStore.Create(data, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        data.UpdatedBy = _auditContext.CurrentUserId;
        _innerStore.Update(data, storeDelegate);
    }

    public void Delete(T data) => _innerStore.Delete(data);

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return Create(data, storeDelegate);
        }
        else
        {
            Update(data, storeDelegate);
            return data.Guid ?? Guid.Empty;
        }
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
