using System.Data.Common;

namespace Birko.Data.SQL.Connectors.Strategies
{
    /// <summary>
    /// Strategy for handling LIKE conditions (Like, StartsWith, EndsWith), and TASK-869's string comparisons with an
    /// explicit <see cref="System.StringComparison"/> (those three and <see cref="Conditions.ConditionType.TextEqual"/>).
    /// </summary>
    public class LikeConditionStrategy : IConditionStrategy
    {
        public bool CanHandle(Conditions.ConditionType type)
        {
            return type is Conditions.ConditionType.Like
                or Conditions.ConditionType.StartsWith
                or Conditions.ConditionType.EndsWith
                or Conditions.ConditionType.TextEqual;
        }

        public string BuildSql(Conditions.Condition condition, DbCommand command, SqlBuilderContext context)
        {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));

            if (string.IsNullOrEmpty(condition.Name))
                throw new InvalidOperationException("Condition name cannot be null or empty");

            if (condition.Comparison is { } comparison)
                return BuildComparison(condition, comparison, command, context);
            if (condition.Type == Conditions.ConditionType.TextEqual)
                throw new InvalidOperationException("A TextEqual condition carries the StringComparison it compares with.");

            var op = condition.IsNot ? " NOT LIKE " : " LIKE ";
            var valueExpression = BuildValueExpression(condition, command, context);

            // SH-M134 (TASK-153): the bound pattern escapes its metacharacters with this character.
            return $"{condition.Name}{op}{valueExpression} ESCAPE '{SqlBuilderContext.LikeEscapeCharacter}'";
        }

        /// <summary>
        /// TASK-869 — <c>Contains</c> / <c>StartsWith</c> / <c>EndsWith</c> / <c>Equals</c> with
        /// <see cref="System.StringComparison.Ordinal"/> or <see cref="System.StringComparison.OrdinalIgnoreCase"/>: the
        /// provider's exact form (<see cref="Conditions.SqlValueFragmentDialect.StringMatch"/>), never the collation's LIKE.
        /// </summary>
        private static string BuildComparison(Conditions.Condition condition, System.StringComparison comparison, DbCommand command,
            SqlBuilderContext context)
        {
            if (condition.IsField)
                throw new NotSupportedException("Cannot translate a string comparison against a column to SQL: compare against a value.");
            string? term = null;
            if (condition.Values != null)
                foreach (var value in condition.Values)
                {
                    term = value as string;
                    break;
                }
            if (term == null)
                throw new NotSupportedException(
                    $"Cannot translate a string comparison with StringComparison.{comparison} without a string to compare with.");

            var index = 0;
            string Bind(object value)
            {
                var name = context.GenerateParameterName(condition.Name!, index++, command);
                context.AddParameter(command, name, value);
                return name;
            }

            var sql = context.Dialect.StringMatch(condition.Type, condition.Name!, term, comparison, Bind);
            return condition.IsNot ? $"NOT ({sql})" : sql;
        }

        private string BuildValueExpression(Conditions.Condition condition, DbCommand command, SqlBuilderContext context)
        {
            if (condition.Values == null)
                return "NULL";

            var enumerator = condition.Values.GetEnumerator();
            if (!enumerator.MoveNext())
                return "NULL";

            var first = enumerator.Current;

            if (!condition.IsField && first != null)
            {
                var formattedValue = context.FormatValue(first, condition.Type);
                var paramName = context.GenerateParameterName(condition.Name!, 0, command);
                context.AddParameter(command, paramName, formattedValue);
                return paramName;
            }
            else
            {
                return first?.ToString() ?? "NULL";
            }
        }
    }
}
