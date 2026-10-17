using Birko.Data.Stores;
using System.Linq;

namespace Birko.Data.Patterns.Paging;

/// <summary>
/// SH-M301 (TASK-171). A page window is only defined over a total order: LIMIT / OFFSET over an order with ties (or
/// none) may return a row on two pages and another on none. Every <see cref="Data.Models.AbstractModel"/> has a unique
/// <c>Guid</c>, so it is appended as the last key. The caller's order is copied, never modified.
/// </summary>
internal static class PagingOrder
{
    public static OrderBy<T> Total<T>(OrderBy<T>? orderBy) where T : Data.Models.AbstractModel
    {
        // TASK-651: the Guid key is found ignoring case, as a field name resolves everywhere else, so ByName("guid") is not
        // appended a second time. (An OrderBy always has a field: its constructor is private and every factory adds one.)
        if (orderBy == null)
            return OrderBy<T>.By(x => x.Guid);
        if (orderBy.Fields.Any(f => string.Equals(f.PropertyName, nameof(Data.Models.AbstractModel.Guid), System.StringComparison.OrdinalIgnoreCase)))
            return orderBy;

        var first = orderBy.Fields[0];
        var total = OrderBy<T>.ByName(first.PropertyName, first.Descending);
        foreach (var field in orderBy.Fields.Skip(1))
            total = total.ThenByName(field.PropertyName, field.Descending);
        return total.ThenBy(x => x.Guid);
    }
}
