using Birko.Data.Expressions;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Shared filter logic for soft-delete decorators.
/// </summary>
public static class SoftDeleteFilter
{
    /// <summary>
    /// Combines a user filter with a DeletedAt == null condition.
    /// </summary>
    public static Expression<Func<T, bool>> CombineWithNotDeleted<T>(Expression<Func<T, bool>>? filter)
        where T : ISoftDeletable
    {
        Expression<Func<T, bool>> notDeleted = x => x.DeletedAt == null;
        return ExpressionParameterReplacer.AndAlso(filter, notDeleted);
    }

    /// <summary>
    /// Stamps <paramref name="item"/> deleted at <paramref name="now"/>; a default stops being one.
    /// </summary>
    /// <remarks>
    /// TASK-638: a deleted row is invisible to the Default decorator's probes, so a marker it kept under the unique index
    /// refused every later promotion, and no retry could help.
    /// </remarks>
    public static void Stamp<T>(T item, DateTime now) where T : ISoftDeletable
    {
        item.DeletedAt = now;
        if (item is Birko.Data.Models.IDefault defaultable) defaultable.IsDefault = false;
        if (item is IDefaultMarker marked) marked.DefaultMarker = null;
    }

    /// <summary>
    /// What a soft delete of <paramref name="data"/> writes: a stamped COPY of the stored row, so every column the caller's
    /// instance does not carry survives; the caller's instance is stamped too, as it always was.
    /// </summary>
    /// <remarks>
    /// TASK-821: the decorators stamped and wrote the caller's instance whole. A view-model repository's <c>Delete</c>
    /// passes a fresh model carrying only what <c>MapToModel</c> assigns, so the soft delete blanked every unmapped
    /// column (measured), and a later <c>Restore</c> brought the row back without them. The copy is a memberwise clone
    /// because the portable stores and caches hand back the instance they hold, and stamping that would change store
    /// state before, and regardless of, the write (SH-H016). With no stored row the caller's instance is written, so the
    /// store reports the missing key as before (<c>EntityNotFoundException</c>, TASK-555).
    /// </remarks>
    public static T ToWrite<T>(T data, T? stored, DateTime now) where T : class, ISoftDeletable
    {
        Stamp(data, now);
        if (stored == null) return data;
        var copy = (T)MemberwiseClone.Invoke(stored, null)!;
        Stamp(copy, now);
        return copy;
    }

    private static readonly System.Reflection.MethodInfo MemberwiseClone =
        typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    /// <summary>The native update of a filtered soft delete: <see cref="Stamp"/> as one statement.</summary>
    public static PropertyUpdate<T> Deletion<T>(DateTime now) where T : Birko.Data.Models.AbstractModel, ISoftDeletable
    {
        var deletion = new PropertyUpdate<T>().Set(x => x.DeletedAt, (DateTime?)now);
        if (typeof(Birko.Data.Models.IDefault).IsAssignableFrom(typeof(T)))
            deletion.Set(Property<T, bool>(nameof(Birko.Data.Models.IDefault.IsDefault)), false);
        if (typeof(IDefaultMarker).IsAssignableFrom(typeof(T)))
            deletion.Set(Property<T, bool?>(nameof(IDefaultMarker.DefaultMarker)), null);
        return deletion;
    }

    private static Expression<Func<T, TProperty>> Property<T, TProperty>(string name)
    {
        var x = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, TProperty>>(Expression.Property(x, name), x);
    }
}
