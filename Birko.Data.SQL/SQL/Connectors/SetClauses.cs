using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// The SET clauses of one UPDATE on the connector's expression path (<c>isExpressionValues: true</c>), each held with
    /// the column it writes. The one producer of a <c>col = expr</c> clause: the statement renders <see cref="Dictionary{TKey,TValue}.Values"/>,
    /// and the IN-list chunk planner reads <see cref="Columns"/>, so the column it checks is the column the statement writes
    /// (TASK-849, rule 16).
    /// </summary>
    public sealed class SetClauses : Dictionary<int, string>
    {
        private readonly Dictionary<int, string> _columns = new();

        /// <summary>Adds <c><paramref name="column"/> = <paramref name="expression"/></c> at <paramref name="index"/>.</summary>
        public void Add(int index, string column, string expression)
        {
            Add(index, column + " = " + expression);
            _columns[index] = column;
        }

        /// <summary>
        /// The columns the clauses write, or null when one of them was added without its column (through the dictionary's
        /// own <c>Add</c>), so what it writes is not known.
        /// </summary>
        public IReadOnlyCollection<string>? Columns
        {
            get
            {
                var columns = new List<string>(Count);
                foreach (var key in Keys)
                {
                    if (!_columns.TryGetValue(key, out var column)) return null;
                    columns.Add(column);
                }
                return columns;
            }
        }

        /// <summary>
        /// The columns an UPDATE with <paramref name="fields"/> writes, or null when they are not known. On the value path
        /// each entry IS the column (the statement renders <c>entry = @SETentry</c>); on the expression path they are known
        /// only from <see cref="SetClauses"/>, and a plain dictionary of rendered clauses is never parsed back.
        /// </summary>
        public static IReadOnlyCollection<string>? WrittenBy(IDictionary<int, string> fields, bool isExpressionValues)
        {
            if (!isExpressionValues) return fields.Values.ToList();
            return (fields as SetClauses)?.Columns;
        }
    }
}
