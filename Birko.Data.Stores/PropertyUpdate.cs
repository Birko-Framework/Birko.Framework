using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Describes a set of property assignments to apply to entities matching a filter.
    /// Platforms can translate these to native operations (SQL SET, MongoDB $set/$inc, etc.)
    /// instead of the read-modify-save pattern.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class PropertyUpdate<T> where T : Models.AbstractModel
    {
        /// <summary>
        /// What <see cref="Increment{TProperty}"/> accepts: the types every provider stores as a number (rule 29).
        /// <c>INumber&lt;T&gt;</c> alone also admits <c>char</c>, <c>Half</c>, <c>Int128</c> and <c>nint</c>, and the
        /// unsigned and single-byte types do not survive every driver (Npgsql binds <c>uint</c> as <c>oid</c> and has
        /// no <c>ulong</c> or <c>sbyte</c>).
        /// </summary>
        private static readonly HashSet<Type> IncrementableTypes = new()
        {
            typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal),
        };

        private readonly List<PropertyAssignment> _assignments = new();

        internal IReadOnlyList<PropertyAssignment> Assignments => _assignments;

        /// <summary>
        /// Sets a property to the specified value.
        /// </summary>
        /// <typeparam name="TProperty">The type of the property.</typeparam>
        /// <param name="property">Expression selecting the property to update.</param>
        /// <param name="value">The new value for the property.</param>
        /// <returns>This instance for fluent chaining.</returns>
        /// <exception cref="InvalidOperationException">The property is already incremented in this update.</exception>
        public PropertyUpdate<T> Set<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
        {
            var assignment = new SetAssignment(property, value);
            RefuseConflict(assignment, set => false, increment => true);
            _assignments.Add(assignment);
            return this;
        }

        /// <summary>
        /// Adds <paramref name="delta"/> to a numeric property: <c>col = col + delta</c>. A negative delta decrements.
        /// <para>
        /// SQL (<c>col = col + @p</c>) and MongoDB (<c>$inc</c>) apply it atomically on the server. Elasticsearch runs a
        /// painless <c>+=</c> per document through UpdateByQuery, which aborts on a version conflict — and the store
        /// does not yet report that (TASK-502), so a contended increment there can be lost. Stores without a native
        /// translation fall back to read-modify-save through <see cref="ApplyTo"/>, which is <b>not</b> atomic.
        /// </para>
        /// <para>
        /// Accepts <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c>, <c>double</c> and <c>decimal</c>. Nullable properties are not
        /// accepted — <c>NULL + 1</c> means something different on every backend. The selector must name a top-level
        /// property directly: a cast such as <c>x =&gt; (int)x.NullableCount</c>, or a nested member, is refused.
        /// </para>
        /// </summary>
        /// <typeparam name="TProperty">The numeric type of the property.</typeparam>
        /// <param name="property">Expression selecting the property to increment.</param>
        /// <param name="delta">The amount to add.</param>
        /// <returns>This instance for fluent chaining.</returns>
        /// <exception cref="ArgumentException">The selector is not a direct top-level property of type <typeparamref name="TProperty"/>, or the type is not supported.</exception>
        /// <exception cref="InvalidOperationException">The property already has an assignment in this update.</exception>
        /// <exception cref="OverflowException">The read-modify-save fallback overflows the property's type.</exception>
        public PropertyUpdate<T> Increment<TProperty>(Expression<Func<T, TProperty>> property, TProperty delta)
            where TProperty : INumber<TProperty>
        {
            if (property.Body is not MemberExpression { Member: PropertyInfo info, Expression: ParameterExpression }
                || info.PropertyType != typeof(TProperty))
            {
                throw new ArgumentException(
                    $"Increment needs a direct access to a top-level property of type {typeof(TProperty).Name}; '{property}' is not one. "
                    + "Casts are refused because they hide the property's real (possibly nullable) type, and nested members "
                    + "because the providers would resolve only the leaf.",
                    nameof(property));
            }

            if (!IncrementableTypes.Contains(typeof(TProperty)))
            {
                throw new ArgumentException(
                    $"Increment supports short, int, long, float, double and decimal; {typeof(TProperty).Name} is not stored "
                    + "numerically by every provider.",
                    nameof(property));
            }

            var assignment = new IncrementAssignment(property, delta, current => checked((TProperty)current + delta));
            RefuseConflict(assignment, set => true, increment => true);
            _assignments.Add(assignment);
            return this;
        }

        /// <summary>
        /// Subtracts <paramref name="delta"/> from a signed numeric property — an alias for
        /// <c>Increment(property, -delta)</c>, stored as an increment. Unsigned types are not accepted, because
        /// their negation wraps round instead of going below zero.
        /// </summary>
        /// <typeparam name="TProperty">The signed numeric type of the property.</typeparam>
        /// <param name="property">Expression selecting the property to decrement.</param>
        /// <param name="delta">The amount to subtract.</param>
        /// <returns>This instance for fluent chaining.</returns>
        /// <exception cref="OverflowException"><paramref name="delta"/> has no negation (e.g. <see cref="int.MinValue"/>).</exception>
        public PropertyUpdate<T> Decrement<TProperty>(Expression<Func<T, TProperty>> property, TProperty delta)
            where TProperty : INumber<TProperty>, ISignedNumber<TProperty>
            => Increment(property, checked(-delta));

        /// <summary>
        /// Applies the property assignments to an entity instance using reflection.
        /// Used as a fallback when native platform translation is not available.
        /// This is read-modify-save and <b>not atomic</b>: an increment applied here can be lost to a concurrent writer.
        /// </summary>
        /// <param name="entity">The entity to apply assignments to.</param>
        internal void ApplyTo(T entity)
        {
            foreach (var assignment in _assignments)
            {
                var prop = assignment.PropertyInfo
                    ?? throw new ArgumentException($"Unable to resolve property from expression: {assignment.Property}");

                var value = assignment.Match(
                    set => set.Value,
                    increment => increment.AddTo(prop.GetValue(entity)!));
                prop.SetValue(entity, value);
            }
        }

        private void RefuseConflict(PropertyAssignment added, Func<SetAssignment, bool> conflictsWithSet, Func<IncrementAssignment, bool> conflictsWithIncrement)
        {
            var path = added.MemberPath;
            if (!_assignments.Any(a => a.MemberPath == path && a.Match(conflictsWithSet, conflictsWithIncrement)))
            {
                return;
            }

            throw new InvalidOperationException(
                $"'{path}' already has an assignment in this update; an increment cannot be combined with another assignment to the same property.");
        }
    }
}
