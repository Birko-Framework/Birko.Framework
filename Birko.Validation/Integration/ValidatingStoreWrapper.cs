using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Linq.Expressions;

namespace Birko.Validation.Integration;

/// <summary>
/// Sync store wrapper that validates entities before Create/Update.
/// Throws <see cref="ValidationException"/> on validation failure.
/// </summary>
public class ValidatingStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel
{
    protected readonly TStore _innerStore;
    protected readonly IValidator<T> _validator;

    public ValidatingStoreWrapper(TStore innerStore, IValidator<T> validator)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public T? Read(Guid guid) => _innerStore.Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null) => _innerStore.Read(filter);
    public long Count(Expression<Func<T, bool>>? filter = null) => _innerStore.Count(filter);

    public Guid Create(T data, StoreDataDelegate<T>? processDelegate = null)
    {
        ValidateAndThrow(data);
        return _innerStore.Create(data, processDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? processDelegate = null)
    {
        ValidateAndThrow(data);
        _innerStore.Update(data, processDelegate);
    }

    public void Delete(T data) => _innerStore.Delete(data);

    public Guid Save(T data, StoreDataDelegate<T>? processDelegate = null)
    {
        ValidateAndThrow(data);
        return _innerStore.Save(data, processDelegate);
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    protected void ValidateAndThrow(T data)
    {
        var result = _validator.Validate(data);
        if (!result.IsValid)
            throw new ValidationException(result);
    }

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
