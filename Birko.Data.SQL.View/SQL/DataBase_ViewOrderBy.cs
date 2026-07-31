using Birko.Data.SQL.Fields;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        /// <summary>
        /// Resolves ORDER BY keys for a view query — view property names as produced by
        /// <see cref="Birko.Data.Stores.OrderBy{T}"/>, including the arbitrary strings
        /// <c>OrderBy&lt;T&gt;.ByName</c> accepts — into the column names the queried view actually exposes.
        /// <para>
        /// TASK-128, the view twin of TASK-110 (SH-H003). Both view ORDER BY emit sites interpolate their keys
        /// into <c>CommandText</c> verbatim, and no layer between the store and those sites resolved anything,
        /// so <c>ByName(request.Sort)</c> put caller text straight into the statement. Measured on SQLite:
        /// the payload <c>"Name; CREATE TABLE Pwned (x INTEGER); --"</c> created that table on the on-the-fly
        /// path AND on the persistent path, neither raising. A key that survives this method is a name read
        /// out of the view's field metadata, never caller text — <b>the resolution IS the whitelist</b>.
        /// </para>
        /// <para>
        /// The same lookup fixes the ordinary-consumer half, which on views is worse than on entities:
        /// <c>OrderBy&lt;TView&gt;.By(x =&gt; x.ViewProp)</c> emitted the VIEW property name while the columns
        /// carry SOURCE names, so it raised <i>no such column</i> — and renaming is the whole point of a view,
        /// so sorting by a view's own property never worked. Both view builders
        /// (<c>SqlViewTranslator</c> and the attribute-driven <c>LoadView</c>) assign the view property to
        /// <see cref="AbstractField.Property"/> while leaving the source column in
        /// <see cref="AbstractField.Name"/>, so one field knows both names.
        /// </para>
        /// </summary>
        /// <param name="view">The view being queried; supplies the field metadata.</param>
        /// <param name="orderFields">Keys to resolve, mapped to true for descending. May be null or empty.</param>
        /// <param name="persistent">
        /// True when the query targets a persistent database VIEW, false for the on-the-fly join select. The
        /// two paths expose columns under DIFFERENT names, so the resolved form has to follow the path: the
        /// on-the-fly SELECT list emits <c>Table.Column</c> for everything, while a persistent view's columns
        /// are the bare source column name — except aggregates, which the view DDL aliases
        /// <c>AS &lt;ViewProperty&gt;</c>. Each key therefore resolves to exactly what that path's own SELECT
        /// list uses.
        /// </param>
        /// <returns>
        /// A dictionary with the same values and iteration order, keyed by resolved column names; the original
        /// reference when there is nothing to resolve.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// A key matches neither a view property name nor a source column name of the view.
        /// </exception>
        public static IDictionary<string, bool>? ResolveViewOrderFields(Tables.View? view, IDictionary<string, bool>? orderFields, bool persistent)
        {
            if (orderFields == null || orderFields.Count == 0)
            {
                return orderFields;
            }

            var fields = view?.GetTableFields().Where(f => f != null).ToArray() ?? Array.Empty<AbstractField>();
            if (fields.Length == 0)
            {
                throw new ArgumentException(
                    $"Cannot resolve view ORDER BY key(s) '{string.Join("', '", orderFields.Keys)}' — the view exposes no fields.",
                    nameof(view));
            }

            var resolved = new Dictionary<string, bool>(orderFields.Count);
            foreach (var kvp in orderFields)
            {
                var field = MatchViewOrderField(fields, kvp.Key);
                if (field == null)
                {
                    throw new ArgumentException(
                        $"ORDER BY key '{kvp.Key}' does not resolve to a column of view '{view!.Name}'. "
                        + "Order by a view property name or its source column name.",
                        nameof(orderFields));
                }

                // Indexer, not Add: a view property and its source column can both be supplied and resolve
                // to the same column, and a duplicate sort key is not worth throwing over.
                resolved[ViewOrderFieldName(field, persistent)] = kvp.Value;
            }
            return resolved;
        }

        /// <summary>
        /// Extracts the ORDER BY key a view sort expression names, applying the same rule
        /// <see cref="Birko.Data.Stores.OrderBy{T}"/> applies — the member's own name, unwrapping a boxing
        /// <see cref="UnaryExpression"/>. The expression is written against the VIEW type, so its member name
        /// is the view property, which is what <see cref="ResolveViewOrderFields"/> expects; taking the name
        /// off a resolved source field instead would hand it the source property and fail to match.
        /// </summary>
        public static string GetViewOrderKey<T, P>(Expression<Func<T, P>> expr)
        {
            if (expr == null)
            {
                throw new ArgumentNullException(nameof(expr));
            }

            var member = expr.Body as MemberExpression
                ?? (expr.Body as UnaryExpression)?.Operand as MemberExpression;
            if (member?.Member is not PropertyInfo propInfo)
            {
                throw new ArgumentException($"Expression '{expr}' must reference a property.", nameof(expr));
            }
            return propInfo.Name;
        }

        /// <summary>
        /// Finds the view field an ORDER BY key names, or null when it names nothing. View property first
        /// (what <see cref="Birko.Data.Stores.OrderBy{T}"/> produces from an expression), then the source
        /// column name — passing the source column worked before this guard existed and has to keep working,
        /// and it comes from the same metadata, so it is equally safe.
        /// </summary>
        private static AbstractField? MatchViewOrderField(IReadOnlyList<AbstractField> fields, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var byProperty = fields.FirstOrDefault(f =>
                f.Property != null && f.Property.Name.Equals(key, StringComparison.Ordinal));
            if (byProperty != null)
            {
                return byProperty;
            }

            return fields.FirstOrDefault(f => f.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The column name the given path's SELECT list exposes for a field — the only correct thing to sort
        /// by, since ORDER BY resolves against the emitted projection.
        /// </summary>
        private static string ViewOrderFieldName(AbstractField field, bool persistent)
        {
            if (!persistent)
            {
                // Mirrors View.GetSelectFields(), which always qualifies with the table name — a join view
                // can carry the same column twice, so the prefix is what keeps the key unambiguous.
                return field.GetSelectName(true);
            }

            // Mirrors View.GetPersistentViewSelectFields(): the view DDL aliases aggregates
            // AS <ViewProperty> and leaves every other column under its source name. Property is declared
            // non-nullable but assigned by the view builders, and a key can reach here matched on Name
            // alone, so fall back rather than risk a NullReferenceException on a sort.
            return field.IsAggregate && field.Property != null ? field.Property.Name : field.Name;
        }
    }
}
