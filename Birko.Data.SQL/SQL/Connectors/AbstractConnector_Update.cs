using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        public void Update(object model, LambdaExpression expr)
        {
            Update(model, DataBase.ParseConditionExpression(expr));
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        public int Update(object model, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return model != null ? Update(model.GetType(), model, conditions) : -1;
        }

        public void Update(Type type, object model, LambdaExpression expr)
        {
            Update(DataBase.LoadTable(type), model, DataBase.ParseConditionExpression(expr));
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        public int Update(Type type, object model, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return Update(DataBase.LoadTable(type), model, conditions);
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        public int Update(Tables.Table table, object model, IEnumerable<Conditions.Condition>? conditions = null)
        {
            if (model == null)
            {
                return -1;
            }
            return Update(table.Name, table.GetSelectFields(), DataBase.Write(table.Fields.Select(f => f.Value), model), conditions);
        }

        public void Update<T, P>(Type type, IDictionary<Expression<Func<T, P>>, object> expresions, LambdaExpression expr)
        {
            Update(type, expresions, DataBase.ParseConditionExpression(expr));
        }

        public void Update<T, P>(Type type, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, LambdaExpression expr)
        {
            Update(type, expresions, DataBase.ParseConditionExpression(expr));
        }

        public void Update<T,P>(Type type, IDictionary<Expression<Func<T, P>>, object> expresions, IEnumerable<Conditions.Condition>? conditions = null)
        {
            var table = DataBase.LoadTable(type);
            Update(table, expresions, conditions);
        }

        public void Update<T, P>(Type type, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, IEnumerable<Conditions.Condition>? conditions = null)
        {
            var table = DataBase.LoadTable(type);
            Update(table, expresions, conditions);
        }

        public void Update<T, P>(Tables.Table table, IDictionary<Expression<Func<T, P>>, object> expresions, IEnumerable<Conditions.Condition>? conditions = null)
        {
            if (table != null)
            {
                Update(table.Name, expresions, conditions);
            }
        }

        public void Update<T, P>(Tables.Table table, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, IEnumerable<Conditions.Condition>? conditions = null)
        {
            if (table != null)
            {
                Update(table.Name, expresions, conditions);
            }
        }

        public void Update<T, P>(string tableName, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, IEnumerable<Conditions.Condition>? conditions = null)
        {
            var fields = new SetClauses();
            var values = new Dictionary<string, object>();
            int i = 0;
            foreach (var kvp in expresions)
            {
                var field = DataBase.GetField(kvp.Key);
                var fieldExpr = DataBase.ParseExpression(kvp.Value, values, false, null, FragmentDialect);
                fields.Add(i, field.Name, fieldExpr ?? string.Empty);
                i++;
            }
            Update(tableName, fields, values, conditions, true);
        }

        public void Update<T, P>(string tableName, IDictionary<Expression<Func<T, P>>, object> expresions, IEnumerable<Conditions.Condition>? conditions = null)
        {
            var fields = new Dictionary<int, string>();
            var values = new Dictionary<string, object>();
            int i = 0;
            foreach (var kvp in expresions)
            {
                var field = DataBase.GetField(kvp.Key);
                fields.Add(i, field.Name);
                values.Add(field.Name, kvp.Value);
                i++;
            }
            Update(tableName, fields, values, conditions);
        }

        public void Update(Tables.Table table, IDictionary<string, object> values, IEnumerable<Conditions.Condition>? conditions = null)
        {
            var tableName = table.Name;
            IDictionary<int, string> fields = table.GetSelectFields();
            Update(tableName, fields, values, conditions);
        }

        /// <summary>
        /// The parameter an UPDATE binds a column's new value to. The one producer of that name: the connector's
        /// own SET rendering and <c>PropertyUpdateSqlTranslator</c>'s expression fragments both use it.
        /// </summary>
        internal static string SetParameterName(string column) => "@SET" + column.Replace(".", string.Empty);

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        /// <remarks>
        /// TASK-833: an <c>IN</c> list past the provider's limit (or the chunk size) is updated chunk by chunk, each chunk
        /// through <see cref="UpdateCore"/> and so guarded on its own (rules 1-3), all of them in ONE boundary (rule 6), and
        /// the matched rows are the sum. Where the list cannot be split it is refused past the limit, before anything runs.
        /// Not virtual (rule 12): an override goes on <see cref="UpdateCore"/>, once per statement.
        /// </remarks>
        public int Update(string tableName, IDictionary<int, string> fields, IDictionary<string, object> values, IEnumerable<Conditions.Condition>? conditions = null, bool isExpressionValues = false, bool allowAllRows = false)
        {
            if (!HasUpdatePayload(fields, values, isExpressionValues))
            {
                return UpdateCore(tableName, fields, values, conditions, isExpressionValues, allowAllRows);
            }
            // SH-H002 — refuse before the transaction wrapper; see AbstractConnector_Delete.
            if (!allowAllRows && WouldTargetEveryRow(conditions))
            {
                throw new Data.Exceptions.WholeTableWriteException("update", tableName);
            }

            var terms = conditions?.ToList();
            var plan = PlanInListChunks("update", terms,
                (command, chunk) => BuildUpdate(command, tableName, fields, values, chunk, isExpressionValues, allowAllRows),
                updateFields: fields, isExpressionValues: isExpressionValues);
            plan.ThrowIfRefused();
            if (!plan.IsChunked)
            {
                return UpdateCore(tableName, fields, values, terms, isExpressionValues, allowAllRows);
            }
            return RunAtomically(() =>
            {
                var matched = 0;
                foreach (var chunk in plan.Chunks!)
                {
                    matched += Math.Max(0, UpdateCore(tableName, fields, values, chunk, isExpressionValues, allowAllRows: false));
                }
                return matched;
            });
        }

        /// <summary>
        /// What "nothing to set" means depends on the path (TASK-331). The value path's payload is <paramref name="values"/>
        /// — an empty one has no parameters for its <c>col = @SETcol</c> list. The expression path's payload is the SET
        /// clauses in <paramref name="fields"/>; <paramref name="values"/> holds only the constants they bound, and
        /// <c>UPPER(Name)</c> or <c>A + B</c> binds none — gating it on <paramref name="values"/> skipped the UPDATE silently.
        /// </summary>
        protected static bool HasUpdatePayload(IDictionary<int, string> fields, IDictionary<string, object>? values, bool isExpressionValues)
            => isExpressionValues ? fields.Any() : values != null && values.Any();

        /// <summary>The one producer of a filtered UPDATE: the run and the chunk planner's probe both build it here.</summary>
        protected void BuildUpdate(System.Data.Common.DbCommand command, string tableName, IDictionary<int, string> fields, IDictionary<string, object>? values, IEnumerable<Conditions.Condition>? conditions, bool isExpressionValues, bool allowAllRows)
        {
            command.CommandText = "UPDATE " + TableIdentifier(tableName) + " SET ";
            if (!isExpressionValues)
            {
                command.CommandText += string.Join(", ", fields.Values.Select(x => x + "= " + SetParameterName(x)));
            }
            else
            {
                command.CommandText += string.Join(", ", fields.Values.Select(x => x));
            }

            AddRequiredWhere(conditions, command, "update", tableName, allowAllRows);
            foreach (var kvp in values ?? new Dictionary<string, object>())
            {
                if (!isExpressionValues)
                {
                    AddParameter(command, SetParameterName(kvp.Key), kvp.Value);
                }
                else
                {
                    AddParameter(command, kvp.Key, kvp.Value);
                }
            }
        }

        /// <summary>Runs ONE update statement (rule 12's seam).</summary>
        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        protected virtual int UpdateCore(string tableName, IDictionary<int, string> fields, IDictionary<string, object> values, IEnumerable<Conditions.Condition>? conditions, bool isExpressionValues, bool allowAllRows)
        {
            var matched = -1;
            if (!HasUpdatePayload(fields, values, isExpressionValues)) return matched;

            // SH-H002 — per statement, so a chunk is guarded exactly as a whole filter is.
            if (!allowAllRows && WouldTargetEveryRow(conditions))
            {
                throw new Data.Exceptions.WholeTableWriteException("update", tableName);
            }

            DoCommandWithTransaction((command) => {
                BuildUpdate(command, tableName, fields, values, conditions, isExpressionValues, allowAllRows);
            }, (command) => {
                matched = command.ExecuteNonQuery();
            }, true);
            return matched;
        }
    }
}
