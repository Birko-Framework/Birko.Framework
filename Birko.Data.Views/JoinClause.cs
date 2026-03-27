using System;

namespace Birko.Data.Views;

/// <summary>
/// Describes a join between two source entities in a view definition.
/// </summary>
public sealed class JoinClause
{
    public Type LeftType { get; }
    public Type RightType { get; }
    public string LeftProperty { get; }
    public string RightProperty { get; }
    public JoinType JoinType { get; }

    public JoinClause(Type leftType, Type rightType, string leftProperty, string rightProperty, JoinType joinType)
    {
        LeftType = leftType;
        RightType = rightType;
        LeftProperty = leftProperty;
        RightProperty = rightProperty;
        JoinType = joinType;
    }
}
