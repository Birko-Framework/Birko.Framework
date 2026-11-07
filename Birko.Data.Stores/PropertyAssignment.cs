using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Stores
{
    /// <summary>
    /// One assignment inside a <see cref="PropertyUpdate{T}"/>: a closed set of kinds (<see cref="SetAssignment"/>,
    /// <see cref="IncrementAssignment"/>, <see cref="ComputedAssignment"/>) whose operand is reached only through
    /// <see cref="Match{TResult}"/>.
    /// Why there is no shared <c>Value</c>: Birko.Data.Stores/CLAUDE.md.
    /// </summary>
    public abstract class PropertyAssignment
    {
        private protected PropertyAssignment(LambdaExpression property)
        {
            Property = property;
        }

        /// <summary>Expression selecting the property being assigned.</summary>
        public LambdaExpression Property { get; }

        /// <summary>Dispatches on the assignment kind.</summary>
        public abstract TResult Match<TResult>(
            Func<SetAssignment, TResult> set,
            Func<IncrementAssignment, TResult> increment,
            Func<ComputedAssignment, TResult> computed);

        /// <summary>The member chain the assignment targets (e.g. <c>Address.Street</c>), casts unwrapped.</summary>
        internal string MemberPath
        {
            get
            {
                var names = new List<string>();
                var expr = Unwrap(Property.Body);
                while (expr is MemberExpression member)
                {
                    names.Insert(0, member.Member.Name);
                    expr = Unwrap(member.Expression);
                }
                return string.Join(".", names);
            }
        }

        internal PropertyInfo? PropertyInfo
            => (Unwrap(Property.Body) as MemberExpression)?.Member as PropertyInfo;

        private static Expression? Unwrap(Expression? expr)
            => expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary ? unary.Operand : expr;
    }

    /// <summary>Assigns a constant: <c>col = value</c>.</summary>
    public sealed class SetAssignment : PropertyAssignment
    {
        internal SetAssignment(LambdaExpression property, object? value) : base(property)
        {
            Value = value;
        }

        /// <summary>The value to assign.</summary>
        public object? Value { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<SetAssignment, TResult> set, Func<IncrementAssignment, TResult> increment,
            Func<ComputedAssignment, TResult> computed)
            => set(this);
    }

    /// <summary>Adds to the current value: <c>col = col + delta</c>. A negative delta decrements.</summary>
    public sealed class IncrementAssignment : PropertyAssignment
    {
        internal IncrementAssignment(LambdaExpression property, object delta, Func<object, object> addTo) : base(property)
        {
            Delta = delta;
            AddTo = addTo;
        }

        /// <summary>The amount to add, typed as the property. Negative for a decrement.</summary>
        public object Delta { get; }

        /// <summary>Adds <see cref="Delta"/> to a current value, in the property's own arithmetic.</summary>
        internal Func<object, object> AddTo { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<SetAssignment, TResult> set, Func<IncrementAssignment, TResult> increment,
            Func<ComputedAssignment, TResult> computed)
            => increment(this);
    }

    /// <summary>
    /// Assigns a <c>decimal</c> formula over the same row: <c>col = round(other / 1.2, 2)</c> (TASK-826). The formula
    /// reads the row as it was before the update; a formula that reads a column the same update assigns is refused when
    /// it is added, so no provider's evaluation order can change the result.
    /// </summary>
    public sealed class ComputedAssignment : PropertyAssignment
    {
        internal ComputedAssignment(LambdaExpression property, ComputedValue value) : base(property)
        {
            Value = value;
        }

        /// <summary>The formula.</summary>
        public ComputedValue Value { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<SetAssignment, TResult> set, Func<IncrementAssignment, TResult> increment,
            Func<ComputedAssignment, TResult> computed)
            => computed(this);
    }
}
