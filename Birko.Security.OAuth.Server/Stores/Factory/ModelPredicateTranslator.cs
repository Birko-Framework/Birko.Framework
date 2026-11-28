using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Security.OAuth.Server.Stores.Factory;

/// <summary>
/// Rewrites a predicate over a model into the same predicate over its stored entity (TASK-1013): every member of the
/// model the predicate reads is replaced by the entity member that stores it. A member that has no entity member of its
/// own (a list or the claims, stored inside a JSON column) is refused by name, never dropped or evaluated in memory —
/// dropping it would widen the predicate (rule 41).
/// </summary>
/// <typeparam name="TModel">The model the caller writes predicates over.</typeparam>
/// <typeparam name="TEntity">The entity the store holds.</typeparam>
internal sealed class ModelPredicateTranslator<TModel, TEntity>
{
    private readonly IReadOnlyDictionary<string, Func<Expression, Expression>> _members;
    private readonly string _storeName;

    /// <param name="storeName">The store, for the refusal message.</param>
    /// <param name="members">
    /// Model member name → the entity expression that stores it, built on the entity parameter. A model member not in the
    /// map is refused.
    /// </param>
    public ModelPredicateTranslator(string storeName, IReadOnlyDictionary<string, Func<Expression, Expression>> members)
    {
        _storeName = storeName;
        _members = members;
    }

    /// <summary>The entity predicate, or null for a null <paramref name="predicate"/>.</summary>
    /// <exception cref="NotSupportedException">The predicate reads a member that has no entity member of its own, or uses
    /// the model parameter other than to read a member.</exception>
    public Expression<Func<TEntity, bool>>? Translate(Expression<Func<TModel, bool>>? predicate)
    {
        if (predicate == null) return null;
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var body = new Visitor(this, predicate.Parameters[0], entity).Visit(predicate.Body);
        return Expression.Lambda<Func<TEntity, bool>>(body, entity);
    }

    private sealed class Visitor : ExpressionVisitor
    {
        private readonly ModelPredicateTranslator<TModel, TEntity> _owner;
        private readonly ParameterExpression _model;
        private readonly ParameterExpression _entity;

        public Visitor(ModelPredicateTranslator<TModel, TEntity> owner, ParameterExpression model, ParameterExpression entity)
        {
            _owner = owner;
            _model = model;
            _entity = entity;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != _model) return base.VisitMember(node);
            if (!_owner._members.TryGetValue(node.Member.Name, out var map))
            {
                throw new NotSupportedException(
                    $"{_owner._storeName} cannot filter on {typeof(TModel).Name}.{node.Member.Name}: it is stored inside a JSON "
                    + "column, so no backend can query it. Filter on a scalar member and test this one on the result.");
            }
            var mapped = map(_entity);
            return mapped.Type == node.Type ? mapped : Expression.Convert(mapped, node.Type);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            // An enum compared in an expression tree reads Convert(model.Member, int); when the entity stores the member
            // as that integer already, the entity member is used as is, not converted to the enum and back.
            if (node.NodeType == ExpressionType.Convert && node.Operand is MemberExpression member && member.Expression == _model
                && _owner._members.TryGetValue(member.Member.Name, out var map))
            {
                var mapped = map(_entity);
                if (mapped.Type == node.Type) return mapped;
            }
            return base.VisitUnary(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _model)
            {
                throw new NotSupportedException(
                    $"{_owner._storeName} can translate a {typeof(TModel).Name} predicate only where it reads the model's members; "
                    + "this one uses the model itself.");
            }
            return base.VisitParameter(node);
        }
    }
}
