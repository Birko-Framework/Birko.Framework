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

            if (usePersistent && !string.IsNullOrEmpty(view.Name))
            {
                return await SelectCountPersistentViewAsync(view.Name!, conditions, ct).ConfigureAwait(false);
            }

            // Pass only the LEFT tables (the join clause supplies the right tables), mirroring the
            // view SELECT command builder. Passing every view table AND the join listed the joined
            // tables twice in FROM, producing a cross join / "ambiguous column" SQL error.
            var leftTables = view.Join?.Select(x => x.Left).Distinct().Where(x => !string.IsNullOrEmpty(x)).ToList();
            return await SelectCountAsync(leftTables ?? view.Tables.Select(x => x.Name), view.Join, conditions, ct).ConfigureAwait(false);
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
