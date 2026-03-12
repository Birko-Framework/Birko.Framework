using System;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Helpers
{
    public class ExpressionBuilder<T>
    {
        private Expression<Func<T, bool>>? _expression;

        public ExpressionBuilder() { }

        public ExpressionBuilder(Expression<Func<T, bool>> expression)
        {
            _expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }

        public ExpressionBuilder<T> And(Expression<Func<T, bool>> right)
        {
            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            _expression = Combine(_expression, right, Expression.AndAlso);
            return this;
        }

        public ExpressionBuilder<T> Or(Expression<Func<T, bool>> right)
        {
            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            _expression = Combine(_expression, right, Expression.OrElse);
            return this;
        }

        public ExpressionBuilder<T> AndIf(bool condition, Expression<Func<T, bool>> right)
        {
            return condition ? And(right) : this;
        }

        public ExpressionBuilder<T> OrIf(bool condition, Expression<Func<T, bool>> right)
        {
            return condition ? Or(right) : this;
        }

        public ExpressionBuilder<T> Not()
        {
            if (_expression == null)
            {
                throw new InvalidOperationException("Cannot negate an empty expression.");
            }

            _expression = Expression.Lambda<Func<T, bool>>(
                Expression.Not(_expression.Body),
                _expression.Parameters
            );
            return this;
        }

        public Expression<Func<T, bool>>? Build()
        {
            return _expression;
        }

        private static Expression<Func<T, bool>> Combine(
            Expression<Func<T, bool>>? left,
            Expression<Func<T, bool>> right,
            Func<Expression, Expression, BinaryExpression> combiner)
        {
            if (left == null)
            {
                return right;
            }

            var parameter = left.Parameters[0];
            var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
            return Expression.Lambda<Func<T, bool>>(
                combiner(left.Body, rightBody),
                parameter
            );
        }

        private class ParameterReplacer : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParameter;
            private readonly ParameterExpression _newParameter;

            public ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
            {
                _oldParameter = oldParameter;
                _newParameter = newParameter;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == _oldParameter ? _newParameter : base.VisitParameter(node);
            }
        }
    }
}
