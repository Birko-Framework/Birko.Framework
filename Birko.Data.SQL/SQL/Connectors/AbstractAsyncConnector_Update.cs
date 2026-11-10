using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractAsyncConnector
    {
        public Task UpdateAsync(object model, LambdaExpression expr, CancellationToken ct = default)
        {
            return UpdateAsync(model, DataBase.ParseConditionExpression(expr), ct);
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        public Task<int> UpdateAsync(object model, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            return model != null ? UpdateAsync(model.GetType(), model, conditions, ct) : Task.FromResult(-1);
        }

        public Task UpdateAsync(Type type, object model, LambdaExpression expr, CancellationToken ct = default)
        {
            return UpdateAsync(LoadUsableTable(type), model, DataBase.ParseConditionExpression(expr), ct);
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        public Task<int> UpdateAsync(Type type, object model, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            return UpdateAsync(LoadUsableTable(type), model, conditions, ct);
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        public Task<int> UpdateAsync(Tables.Table table, object model, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            RequireUsableTable(table); // TASK-891
            if (model == null)
            {
                return Task.FromResult(-1);
            }
            return UpdateAsync(table.Name, table.GetSelectFields(), DataBase.Write(table.Fields.Select(f => f.Value), model), conditions, false, ct);
        }

        public Task UpdateAsync<T, P>(Type type, IDictionary<Expression<Func<T, P>>, object> expresions, LambdaExpression expr, CancellationToken ct = default)
        {
            return UpdateAsync(type, expresions, DataBase.ParseConditionExpression(expr), ct);
        }

        public Task UpdateAsync<T, P>(Type type, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, LambdaExpression expr, CancellationToken ct = default)
        {
            return UpdateAsync(type, expresions, DataBase.ParseConditionExpression(expr), ct);
        }

        public Task UpdateAsync<T, P>(Type type, IDictionary<Expression<Func<T, P>>, object> expresions, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            var table = LoadUsableTable(type);
            return UpdateAsync(table, expresions, conditions, ct);
        }

        public Task UpdateAsync<T, P>(Type type, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            var table = LoadUsableTable(type);
            return UpdateAsync(table, expresions, conditions, ct);
        }

        public Task UpdateAsync<T, P>(Tables.Table table, IDictionary<Expression<Func<T, P>>, object> expresions, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            if (table != null)
            {
                RequireUsableTable(table); // TASK-891
                return UpdateAsync(table.Name, expresions, conditions, ct);
            }
            return Task.CompletedTask;
        }

        public Task UpdateAsync<T, P>(Tables.Table table, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            if (table != null)
            {
                RequireUsableTable(table); // TASK-891
                return UpdateAsync(table.Name, expresions, conditions, ct);
            }
            return Task.CompletedTask;
        }

        public Task UpdateAsync<T, P>(string tableName, IDictionary<Expression<Func<T, P>>, Expression<Func<T, P>>> expresions, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
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
            return UpdateAsync(tableName, fields, values, conditions, true, ct);
        }

        public Task UpdateAsync<T, P>(string tableName, IDictionary<Expression<Func<T, P>>, object> expresions, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
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
            return UpdateAsync(tableName, fields, values, conditions, false, ct);
        }

        public Task UpdateAsync(Tables.Table table, IDictionary<string, object> values, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            RequireUsableTable(table); // TASK-891
            var tableName = table.Name;
            IDictionary<int, string> fields = table.GetSelectFields();
            return UpdateAsync(tableName, fields, values, conditions, false, ct);
        }

        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        /// <remarks>
        /// TASK-833 — the async twin of the sync <c>Update(string, …)</c>'s chunking. Not virtual since TASK-833 (rule 12): an
        /// override goes on <see cref="UpdateCoreAsync"/>, once per statement. None was found in the framework or the
        /// consumer repos.
        /// </remarks>
        public async Task<int> UpdateAsync(string tableName, IDictionary<int, string> fields, IDictionary<string, object> values, IEnumerable<Conditions.Condition>? conditions = null, bool isExpressionValues = false, CancellationToken ct = default, bool allowAllRows = false)
        {
            // TASK-891 — see the sync twin.
            RequireUsableStatementColumns(new[] { tableName }, SetClauses.WrittenBy(fields, isExpressionValues), conditions);
            if (!HasUpdatePayload(fields, values, isExpressionValues))
            {
                return await UpdateCoreAsync(tableName, fields, values, conditions, isExpressionValues, allowAllRows, ct).ConfigureAwait(false);
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
                return await UpdateCoreAsync(tableName, fields, values, terms, isExpressionValues, allowAllRows, ct).ConfigureAwait(false);
            }
            return await RunAtomicallyAsync(async () =>
            {
                var matched = 0;
                foreach (var chunk in plan.Chunks!)
                {
                    matched += Math.Max(0, await UpdateCoreAsync(tableName, fields, values, chunk, isExpressionValues, false, ct).ConfigureAwait(false));
                }
                return matched;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>Runs ONE update statement (rule 12's seam).</summary>
        /// <returns>Rows the statement matched, as the driver reports them (TASK-555); -1 when nothing was sent.</returns>
        protected virtual async Task<int> UpdateCoreAsync(string tableName, IDictionary<int, string> fields, IDictionary<string, object> values, IEnumerable<Conditions.Condition>? conditions, bool isExpressionValues, bool allowAllRows, CancellationToken ct)
        {
            var matched = -1;
            if (!HasUpdatePayload(fields, values, isExpressionValues)) return matched;

            // SH-H002 — per statement, so a chunk is guarded exactly as a whole filter is.
            if (!allowAllRows && WouldTargetEveryRow(conditions))
            {
                throw new Data.Exceptions.WholeTableWriteException("update", tableName);
            }

            await DoCommandWithTransactionAsync(async (command) =>
            {
                BuildUpdate(command, tableName, fields, values, conditions, isExpressionValues, allowAllRows);
                await Task.CompletedTask;
            }, async (command) =>
            {
                matched = await command.ExecuteNonQueryAsync(ct);
            }, true);
            return matched;
        }
    }
}
