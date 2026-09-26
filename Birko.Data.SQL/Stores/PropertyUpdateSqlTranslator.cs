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
        internal static (Dictionary<int, string> Fields, Dictionary<string, object> Values) Translate<T>(PropertyUpdate<T> updates)
            where T : Models.AbstractModel
        {
            var fields = new Dictionary<int, string>();
            var values = new Dictionary<string, object>();
            int i = 0;
            foreach (var assignment in updates.Assignments)
            {
                var column = SQL.DataBase.GetFieldFromLambda(assignment.Property).Name;
                var parameter = Connectors.AbstractConnector.SetParameterName(column);
                var (fragment, value) = assignment.Match(
                    set => ($"{column} = {parameter}", set.Value),
                    increment => ($"{column} = {column} + {parameter}", (object?)increment.Delta));
                fields.Add(i, fragment);
                values.Add(parameter, value ?? DBNull.Value);
                i++;
            }
            return (fields, values);
        }
    }
}
