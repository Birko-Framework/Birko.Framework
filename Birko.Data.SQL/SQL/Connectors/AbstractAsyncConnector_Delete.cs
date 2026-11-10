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
        public Task DeleteAsync(Type type, LambdaExpression expr, CancellationToken ct = default)
        {
            return DeleteAsync(type, DataBase.ParseConditionExpression(expr), ct);
        }

        public Task DeleteAsync(Type type, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            return DeleteAsync(LoadUsableTable(type), conditions, ct);
        }

        public Task DeleteAsync(Tables.Table table, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            RequireUsableTable(table); // TASK-891
            var tableName = table.Name;
            return DeleteAsyncAsync(tableName, conditions, ct);
        }

        /// <inheritdoc cref="AbstractConnector.DeleteAll(Type)"/>
        public Task DeleteAllAsync(Type type, CancellationToken ct = default)
        {
            return DeleteAsyncAsync(LoadUsableTable(type).Name, conditions: null, ct, allowAllRows: true);
        }

        private async Task DeleteAsyncAsync(string tableName, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default, bool allowAllRows = false)
        {
            // TASK-891 — see the sync twin.
            RequireUsableStatementColumns(new[] { tableName }, null, conditions);
            // SH-H002 — refuse before the transaction wrapper; see AbstractConnector_Delete.
            if (!allowAllRows && WouldTargetEveryRow(conditions))
            {
                throw new Data.Exceptions.WholeTableWriteException("delete", tableName);
            }

            // TASK-833 — see the sync twin in AbstractConnector_Delete.
            var terms = conditions?.ToList();
            var plan = PlanInListChunks("delete", terms, (command, chunk) => BuildDelete(command, tableName, chunk, allowAllRows));
            plan.ThrowIfRefused();
            if (!plan.IsChunked)
            {
                await DeleteStatementAsync(tableName, terms, allowAllRows, ct).ConfigureAwait(false);
                return;
            }
            await RunAtomicallyAsync(async () =>
            {
                foreach (var chunk in plan.Chunks!)
                {
                    await DeleteStatementAsync(tableName, chunk, allowAllRows: false, ct).ConfigureAwait(false);
                }
                return 0;
            }, ct).ConfigureAwait(false);
        }

        private async Task DeleteStatementAsync(string tableName, IEnumerable<Conditions.Condition>? conditions, bool allowAllRows, CancellationToken ct)
        {
            // SH-H002 — per statement, so a chunk is guarded exactly as a whole filter is.
            if (!allowAllRows && WouldTargetEveryRow(conditions))
            {
                throw new Data.Exceptions.WholeTableWriteException("delete", tableName);
            }

            await DoCommandWithTransactionAsync(async (command) =>
            {
                BuildDelete(command, tableName, conditions, allowAllRows);
                await Task.CompletedTask;
            }, async (command) =>
            {
                await command.ExecuteNonQueryAsync(ct);
            }, true);
        }
    }
}
