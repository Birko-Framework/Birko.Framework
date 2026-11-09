using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractAsyncConnector
    {
        public Task<long> SelectCountAsync(Type type, LambdaExpression? expr, CancellationToken ct = default)
        {
            return SelectCountAsync(new[] { type }, expr, ct);
        }

        public Task<long> SelectCountAsync(IEnumerable<Type> types, LambdaExpression? expr = null, CancellationToken ct = default)
        {
            return SelectCountAsync(types, (expr != null) ? DataBase.ParseConditionExpression(expr) : null, ct);
        }

        public Task<long> SelectCountAsync(Type type, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            return SelectCountAsync(new[] { type }, conditions, ct);
        }

        public Task<long> SelectCountAsync(IEnumerable<Type> types, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            return (types != null) ? SelectCountAsync(types.Select(LoadUsableTable).ToArray(), conditions, ct) : Task.FromResult(0L);
        }

        public Task<long> SelectCountAsync(IEnumerable<Tables.Table> tables, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            // TASK-724: a null table is skipped, as the sync overload does; it threw NullReferenceException (measured).
            return (tables != null) ? SelectCountAsync(tables.Where(x => x != null).Select(x => x.Name), conditions, ct) : Task.FromResult(0L);
        }

        public Task<long> SelectCountAsync(IEnumerable<string> tableNames, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            return SelectCountAsync(tableNames, null, conditions, ct);
        }

        /// <summary>
        /// The async twin of <see cref="AbstractConnector.SelectCount(IEnumerable{string}, IEnumerable{Conditions.Join}, IEnumerable{Conditions.Condition})"/>,
        /// with its TASK-833 chunking. Not virtual since TASK-833 (rule 12): an override goes on
        /// <see cref="SelectCountCoreAsync"/>, once per statement. None was found in the framework or the consumer repos.
        /// </summary>
        public async Task<long> SelectCountAsync(IEnumerable<string> tableNames, IEnumerable<Conditions.Join>? joinconditions = null, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            if (tableNames == null || !tableNames.Any(x => !string.IsNullOrEmpty(x))) return 0;
            var names = tableNames.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            var joins = joinconditions?.ToList();
            var terms = conditions?.ToList();
            var plan = PlanInListChunks("count", terms, (command, chunk) => CreateCountCommand(command, names, joins, chunk));
            plan.ThrowIfRefused();

            long count = 0;
            foreach (var chunk in plan.Statements(terms))
            {
                count += await SelectCountCoreAsync(names, joins, chunk, ct).ConfigureAwait(false);
            }
            return count;
        }

        /// <summary>Runs ONE count statement over non-empty table names (rule 12's seam).</summary>
        protected virtual async Task<long> SelectCountCoreAsync(IEnumerable<string> tableNames, IEnumerable<Conditions.Join>? joinconditions, IEnumerable<Conditions.Condition>? conditions, CancellationToken ct)
        {
            long count = 0;
            try
            {
                await DoCommandAsync(async (command) =>
                {
                    command = CreateCountCommand(command, tableNames, joinconditions, conditions);
                    await Task.CompletedTask;
                }, async (command) =>
                {
                    var data = await command.ExecuteScalarAsync(ct);
                    count = data != null ? Convert.ToInt64(data) : 0;
                }, false, ct);   // TASK-724: the token reaches the connection's open too
            }
            catch (Exception ex) when (IsMissingTableExceptionChain(ex))
            {
                // TASK-285 — the count of a table that does not exist is 0, exactly as the list of its
                // rows is empty. See the sync overload for why; the reasoning is not repeated.
                //
                // TASK-287 — and the escape is recorded here too. This is a SEPARATE code path from
                // the sync overload, not a wrapper over it, so a fix applied to one of the two looks
                // green while leaving the other exactly as it was; that asymmetry is the recurring
                // shape of defect in this area. Both call the same producer.
                //
                // ⚠ TASK-288 moved that recording one layer earlier and this call site is gone. The
                // anomaly is now detected once, in EnsureSchemaAndReport, where the annotation is
                // written — because the same detection also has to bump SchemaGeneration so a store
                // whose table vanished stops trusting its remembered initialization. Recording here as
                // well would have been unreachable in practice: the annotation only exists because
                // that handler ran, so anything this call could record it had already recorded.
                return 0;
            }
            return count;
        }
    }
}
