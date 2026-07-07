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

            var usePersistent = ShouldUsePersistentView(view, name => ViewExists(name));

            if (usePersistent)
            {
                await foreach (var items in RunReaderCommandAsync(
                    (command) => { CreatePersistentViewSelectCommand(command, view, conditions, orderFields, limit, offset); return Task.CompletedTask; },
                    (reader) => Task.FromResult<IEnumerable<object>>(new object[1] { transformFunction?.Invoke(view.GetPersistentViewSelectFields(), reader)! }),
                    ct).ConfigureAwait(false))
                {
                    yield return items?.FirstOrDefault()!;
                }
            }
            else
            {
                await foreach (var items in RunReaderCommandAsync(
                    (command) => { CreateSelectCommand(command, view, conditions, orderFields, limit, offset); return Task.CompletedTask; },
                    (reader) => Task.FromResult<IEnumerable<object>>(new object[1] { transformFunction?.Invoke(view.GetSelectFields(), reader)! }),
                    ct).ConfigureAwait(false))
                {
                    yield return items?.FirstOrDefault()!;
                }
            }
        }
    }
}
