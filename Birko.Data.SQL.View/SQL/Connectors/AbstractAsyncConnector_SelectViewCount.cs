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

            var usePersistent = ShouldUsePersistentView(view, name => ViewExists(name));
            // TASK-639: a persistent view's WHERE names its own columns, not the source ones.
            conditions = DataBase.ResolveViewConditions(view, conditions, usePersistent);

            if (usePersistent && !string.IsNullOrEmpty(view.Name))
            {
                return await SelectCountPersistentViewAsync(view.Name!, conditions, ct).ConfigureAwait(false);
            }

            // TASK-151 (SH-M404): COUNT(*) over the view's own SELECT (see CreateViewCountCommand).
            long count = 0;
            await DoCommandAsync(
                command => { CreateViewCountCommand(command, view, conditions); return Task.CompletedTask; },
                async command =>
                {
                    var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (result != null && result != DBNull.Value) count = Convert.ToInt64(result);
                }).ConfigureAwait(false);
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
