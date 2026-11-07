using System.Collections.Generic;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        /// <summary>
        /// TASK-837. Plans a view read's <c>IN</c> chunking through the same planner the table reads use
        /// (<see cref="AbstractConnectorBase.PlanInListChunks"/>), so a view has no rules of its own. The probe builds the
        /// command the read will run: the persistent view's SELECT, or the on-the-fly join SELECT.
        /// </summary>
        /// <remarks>
        /// An on-the-fly view with aggregate fields filters in WHERE, before its GROUP BY, so a group's rows could span
        /// chunks; it is never split, as a table aggregate is not. A persistent view filters its own output rows, so a
        /// grouped persistent view splits like any other read.
        /// </remarks>
        private protected InListChunkPlan PlanViewReadChunks(
            Tables.View view,
            bool usePersistent,
            IReadOnlyList<Conditions.Condition>? terms,
            IDictionary<string, bool>? orderFields,
            int? limit,
            int? offset)
        {
            return PlanInListChunks("view read", terms,
                (command, chunk) =>
                {
                    if (usePersistent) CreatePersistentViewSelectCommand(command, view, chunk, orderFields, limit, offset);
                    else CreateSelectCommand(command, view, chunk, orderFields, limit, offset);
                },
                SortOrPageReason(orderFields, limit, offset) ?? ViewAggregateReason(view, usePersistent));
        }

        /// <summary>TASK-837. The count twin of <see cref="PlanViewReadChunks"/>; the view's count is the sum of the chunks.</summary>
        private protected InListChunkPlan PlanViewCountChunks(Tables.View view, bool usePersistent, IReadOnlyList<Conditions.Condition>? terms)
        {
            return PlanInListChunks("view count", terms,
                (command, chunk) =>
                {
                    if (usePersistent) CreatePersistentViewSelectCountCommand(command, view.Name!, chunk);
                    else CreateViewCountCommand(command, view, chunk);
                },
                ViewAggregateReason(view, usePersistent));
        }

        private static string? ViewAggregateReason(Tables.View view, bool usePersistent)
            => !usePersistent && view.HasAggregateFields()
                ? "the view is an aggregate filtered before its GROUP BY, whose groups would span chunks"
                : null;
    }
}
