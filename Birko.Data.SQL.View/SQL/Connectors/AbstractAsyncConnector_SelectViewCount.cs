using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractAsyncConnector
    {
        /// <summary>
        /// Async counterpart of <see cref="AbstractConnector.SelectCount(Tables.View, IEnumerable{Conditions.Condition})"/>.
        /// Runs the count via the async command path so the calling thread is not blocked and the
        /// cancellation token is honored (CR-H096).
        /// </summary>
        public async Task<long> SelectCountAsync(Tables.View view, IEnumerable<Conditions.Condition>? conditions = null, CancellationToken ct = default)
        {
            if (view == null)
            {
                return 0;
            }

            var usePersistent = await ShouldUsePersistentViewAsync(view, name => ViewExistsAsync(name, ct)).ConfigureAwait(false);
            // TASK-639: a persistent view's WHERE names its own columns, not the source ones.
            var terms = DataBase.ResolveViewConditions(view, conditions, usePersistent)?.ToList();

            // TASK-837 — see the sync twin.
            var plan = PlanViewCountChunks(view, usePersistent, terms);
            plan.ThrowIfRefused();

            long count = 0;
            foreach (var chunk in plan.Statements(terms))
            {
                // TASK-724: no name test — see the sync twin (rule 53: an unreachable fallback, removed).
                if (usePersistent)
                {
                    count += await SelectCountPersistentViewAsync(view.Name!, chunk, ct).ConfigureAwait(false);
                    continue;
                }

                // TASK-151 (SH-M404): COUNT(*) over the view's own SELECT (see CreateViewCountCommand).
                await DoCommandAsync(
                    command => { CreateViewCountCommand(command, view, chunk); return Task.CompletedTask; },
                    async command =>
                    {
                        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                        if (result != null && result != DBNull.Value) count += Convert.ToInt64(result);
                    }).ConfigureAwait(false);
            }
            return count;
        }

        private async Task<long> SelectCountPersistentViewAsync(string viewName, IEnumerable<Conditions.Condition>? conditions, CancellationToken ct)
        {
            long count = 0;
            await DoCommandAsync(
                (command) => { CreatePersistentViewSelectCountCommand(command, viewName, conditions); return Task.CompletedTask; },
                async (command) =>
                {
                    var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value)
                    {
                        count = Convert.ToInt64(result);
                    }
                }).ConfigureAwait(false);
            return count;
        }
    }
}
