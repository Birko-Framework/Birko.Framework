using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.SQL.Conditions
{
    public enum JoinType
    {
        Cross,
        Inner,
        LeftOuter
    }

    public class Join
    {
        public string Left { get; set; } = null!;
        public string Right { get; set; } = null!;
        public IEnumerable<Condition>? Conditions { get; set; }
        public JoinType JoinType { get; set; }

        /// <param name="joinType">
        /// The join type; when omitted, <see cref="JoinType.Inner"/> if <paramref name="conditions"/> has any and
        /// <see cref="JoinType.Cross"/> if not. TASK-626 (SH-M147): the default was always Cross, and a Cross join's
        /// conditions were not rendered, so a caller who gave conditions and no type got a cartesian product.
        /// </param>
        public Join(string left, string right, JoinType? joinType = null, IEnumerable<Condition>? conditions = null)
        {
            Left = left;
            Right = right;
            AddConditions(conditions);
            JoinType = joinType ?? (Conditions != null && Conditions.Any() ? JoinType.Inner : JoinType.Cross);
        }

        public static Join Create(string left, string right, JoinType? joinType = null, IEnumerable<Condition>? conditions = null)
        {
            return new Join(left, right, joinType, conditions);
        }

        /// <summary>A join on <paramref name="condition"/>: <see cref="JoinType.Inner"/> unless another type is given.</summary>
        public static Join Create(string left, string right, Condition condition, JoinType joinType = JoinType.Inner)
        {
            return new Join(left, right, joinType, new[] { condition });
        }

        /// <summary>
        /// The keyword that joins <paramref name="right"/>, refusing a join whose type and conditions contradict each other.
        /// The one producer for both join renderers (the select builder and the view builder, TASK-626).
        /// </summary>
        /// <remarks>
        /// SH-M147: a CROSS join's conditions were dropped, so it rendered as a cartesian product; it is refused, since a
        /// cross join has no ON. SH-M148: a group with no conditions was skipped, so a conditionless CROSS JOIN (the only
        /// form one has) vanished from the FROM clause, and an INNER or LEFT join whose conditions failed to translate
        /// vanished with it; the first is rendered, the second refused.
        /// </remarks>
        public static string Keyword(JoinType joinType, bool hasConditions, string left, string right)
        {
            switch (joinType)
            {
                case JoinType.Cross when hasConditions:
                    throw new NotSupportedException(
                        $"The CROSS join of '{left}' and '{right}' carries conditions, and a cross join has no ON: it would be a "
                        + "cartesian product. Declare it Inner or LeftOuter.");
                case JoinType.Cross:
                    return " CROSS JOIN ";
                case JoinType.Inner when hasConditions:
                    return " INNER JOIN ";
                case JoinType.LeftOuter when hasConditions:
                    return " LEFT OUTER JOIN ";
                default:
                    throw new NotSupportedException(
                        $"The {joinType} join of '{left}' and '{right}' has no condition, so it cannot be rendered: dropping it "
                        + "would change the result, and an ON with nothing in it is not SQL.");
            }
        }

        public Join AddConditions(IEnumerable<Condition>? conditions)
        {
            if (conditions != null && conditions.Any())
            {
                foreach(var condition in conditions)
                {
                    AddCondition(condition);
                }
            }
            return this;
        }

        public Join AddCondition(Condition condition)
        {
            if (condition != null)
            {
                Conditions = (Conditions != null) ? Conditions.Concat(new[] { condition }) : new[] { condition };
            }
            return this;
        }
    }
}
