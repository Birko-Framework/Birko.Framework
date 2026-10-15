using System;
using System.Collections.Generic;
using Birko.Data.Stores;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Renders a <see cref="PropertyUpdate{T}"/> as the SET fragments of one UPDATE statement, for the connector's
    /// <c>isExpressionValues: true</c> path. Shared by the sync and async bulk stores so the two cannot drift.
    /// Column names come from table metadata and are emitted bare (rule 17); the connector quotes the table.
    /// </summary>
    internal static class PropertyUpdateSqlTranslator
    {
        internal static (Dictionary<int, string> Fields, Dictionary<string, object> Values) Translate<T>(
            PropertyUpdate<T> updates, Connectors.AbstractConnectorBase connector)
            where T : Models.AbstractModel
        {
            var fields = new Dictionary<int, string>();
            var values = new Dictionary<string, object>();
            var columnPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var columnMembers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (var assignment in updates.Assignments)
            {
                var field = SQL.DataBase.GetFieldFromLambda(assignment.Property);
                var column = field.Name;

                // SH-M030 (TASK-170): two Sets of one property added one parameter twice and threw, where the portable
                // path applies them in order (the last wins). The same rule here. Two DIFFERENT properties mapped onto
                // one column are no such sequence, so they are refused.
                if (columnPositions.TryGetValue(column, out var earlier))
                {
                    if (columnMembers[column] != assignment.MemberPath)
                        throw new InvalidOperationException(
                            $"'{columnMembers[column]}' and '{assignment.MemberPath}' both map to column {column}; an update cannot set it twice.");
                    fields.Remove(earlier);
                    values.Remove(Connectors.AbstractConnector.SetParameterName(column));
                }
                columnPositions[column] = i;
                columnMembers[column] = assignment.MemberPath;
                var parameter = Connectors.AbstractConnector.SetParameterName(column);
                var (fragment, value) = assignment.Match(
                    // Through the field's own conversion, exactly as an entity write (TASK-596).
                    set => ($"{column} = {parameter}", field.ToStoredValue(set.Value)),
                    increment => ($"{column} = {connector.IncrementExpression(field, column, parameter)}", (object?)increment.Delta));
                fields.Add(i, fragment);
                values.Add(parameter, value ?? DBNull.Value);
                i++;
            }
            return (fields, values);
        }
    }
}
