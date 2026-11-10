using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractAsyncConnector
    {
        /// <summary>
        /// Async counterpart of <see cref="AbstractConnector.Select(Tables.View, Func{IDictionary{int, string}, DbDataReader, object}, IEnumerable{Conditions.Condition}, IDictionary{string, bool}, int?, int?)"/>.
        /// Streams the view query through the real async reader so the calling thread is not blocked and
        /// the cancellation token is honored during DB work (CR-H096).
        /// </summary>
        public async IAsyncEnumerable<object> SelectAsync(
            Tables.View view,
            Func<IDictionary<int, string>, DbDataReader, object>? transformFunction = null,
            IEnumerable<Conditions.Condition>? conditions = null,
            IDictionary<string, bool>? orderFields = null,
            int? limit = null,
            int? offset = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (view == null)
            {
                yield break;
            }

            var usePersistent = await ShouldUsePersistentViewAsync(view, name => ViewExistsAsync(name, ct)).ConfigureAwait(false);

            // TASK-128: resolve here, after usePersistent is known — see the sync twin in
            // AbstractConnector_SelectView.cs for why this is the single resolution point.
            orderFields = DataBase.ResolveViewOrderFields(view, orderFields, usePersistent, FragmentDialect);
            // TASK-639: the WHERE follows the path too; a persistent view has no source columns.
            var terms = DataBase.ResolveViewConditions(view, conditions, usePersistent)?.ToList();

            // TASK-837 — see the sync twin.
            var plan = PlanViewReadChunks(view, usePersistent, terms, orderFields, limit, offset);
            plan.ThrowIfRefused();

            // TASK-891 — a missing relation is reported, as the count reports it; see the sync twin.
            foreach (var chunk in plan.Statements(terms))
            {
                if (usePersistent)
                {
                    await foreach (var items in RunReaderCommandAsync(
                        (command) => { CreatePersistentViewSelectCommand(command, view, chunk, orderFields, limit, offset); return Task.CompletedTask; },
                        (reader) => Task.FromResult<IEnumerable<object>>(new object[1] { transformFunction?.Invoke(view.GetPersistentViewSelectFields(), reader)! }),
                        ct, missingTableIsEmpty: false).ConfigureAwait(false))
                    {
                        yield return items?.FirstOrDefault()!;
                    }
                }
                else
                {
                    await foreach (var items in RunReaderCommandAsync(
                        (command) => { CreateSelectCommand(command, view, chunk, orderFields, limit, offset); return Task.CompletedTask; },
                        (reader) => Task.FromResult<IEnumerable<object>>(new object[1] { transformFunction?.Invoke(view.GetSelectFields(), reader)! }),
                        ct, missingTableIsEmpty: false).ConfigureAwait(false))
                    {
                        yield return items?.FirstOrDefault()!;
                    }
                }
            }
        }
    }
}
