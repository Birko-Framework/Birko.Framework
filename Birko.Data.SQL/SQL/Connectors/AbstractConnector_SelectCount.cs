using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        public long SelectCount(Type type, LambdaExpression? expr)
        {
            return SelectCount(new[] { type }, expr);
        }

        public long SelectCount(IEnumerable<Type> types, LambdaExpression? expr = null)
        {
            return SelectCount(types, (expr != null) ? DataBase.ParseConditionExpression(expr) : null);
        }

        public long SelectCount(Type type, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return SelectCount(new[] { type }, conditions);
        }

        public long SelectCount(IEnumerable<Type> types, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return (types != null) ? SelectCount(types.Select(LoadUsableTable).ToArray(), conditions) : 0;
        }

        public long SelectCount(IEnumerable<Tables.Table> tables, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return (tables != null) ? SelectCount(tables.Where(x => x != null).Select(x => x.Name), conditions) : 0;
        }

        public long SelectCount(IEnumerable<string> tableNames, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return SelectCount(tableNames, null, conditions);
        }

        /// <summary>
        /// Counts the rows the conditions match. TASK-833: an <c>IN</c> list past the provider's limit (or the chunk size) is
        /// counted chunk by chunk and the counts summed. The chunks are disjoint: the planner drops duplicates and splits only
        /// lists whose .NET equality is the database's. A list that cannot be split is refused past the limit, before
        /// anything runs. A chunked count outside a transaction is not a single snapshot: a write committed between two
        /// chunks can be counted by one and missed by the other. Not virtual (rule 12): an override goes on <see cref="SelectCountCore"/>, once per statement, and
        /// cannot skip the planning.
        /// </summary>
        public long SelectCount(IEnumerable<string> tableNames, IEnumerable<Conditions.Join>? joinconditions = null, IEnumerable<Conditions.Condition>? conditions = null)
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
                count += SelectCountCore(names, joins, chunk);
            }
            return count;
        }

        /// <summary>The one producer of a count statement: the run and the chunk planner's probe both build it here.</summary>
        protected DbCommand CreateCountCommand(DbCommand command, IEnumerable<string> tableNames, IEnumerable<Conditions.Join>? joinconditions, IEnumerable<Conditions.Condition>? conditions)
            => CreateSelectCommand(command, tableNames, new Dictionary<int, string> { { 0, "count(*) as count" } }, joinconditions, conditions);

        /// <summary>Runs ONE count statement over non-empty table names (rule 12's seam).</summary>
        protected virtual long SelectCountCore(IEnumerable<string> tableNames, IEnumerable<Conditions.Join>? joinconditions, IEnumerable<Conditions.Condition>? conditions)
        {
            long count = 0;
            try
            {
                DoCommand((command) => {
                    command = CreateCountCommand(command, tableNames, joinconditions, conditions);
                }, (command) =>
                {
                    var data = command.ExecuteScalar();
                    count = data != null ? Convert.ToInt64(data) : 0;
                });
            }
            catch (Exception ex) when (IsMissingTableExceptionChain(ex))
            {
                // TASK-285 — THE COUNT OF A TABLE THAT DOES NOT EXIST IS 0, exactly as the list of its
                // rows is empty.
                //
                // A reader already answers this condition with `yield break` (AbstractConnector.cs:452,
                // async :378) — IsMissingTableException exists, in its own words, so "a reader can yield
                // an empty result instead of faulting". A COUNT is a read. Before this, it was the one
                // read that faulted instead, so the same missing table produced an empty list on one
                // route and a 500 on another, decided purely by the statement's shape.
                //
                // ⚠ SCOPED TO COUNTS, AND DELIBERATELY NOT TO EnsureSchemaAndReport. That method throws
                // because TASK-277 measured WRITES being silently discarded — Create returning a real
                // Guid against a table that was never created. Writes must keep reporting; widening
                // this catch to them reopens that defect.
                //
                // ⚠ CHAIN, not the direct predicate: EnsureSchemaAndReport rethrows as
                // `new Exception(commandText, ex)`, so the outer message is the SQL and a message-only
                // check silently never matches (see IsMissingTableExceptionChain).
                //
                // Raised by Symbio TASK-602, where this surfaced as an intermittent 500 on a fresh
                // deployment. It does NOT explain why the table was missing — that question stays open
                // there — it makes the answer correct either way.
                //
                // TASK-287 — but the answer is not the whole story, and the annotation TASK-286
                // produces travels ON THE THROWN EXCEPTION, which this catch has just consumed. So
                // the one case worth seeing was being produced and discarded here: measured against a
                // live Symbio API on 2026-08-31, a COUNT answered 200 with totalCount 0 and logged
                // ZERO lines while a write in the identical condition answered 500 and logged the
                // annotation — and both occurrences Symbio TASK-602 ever recorded were counts.
                //
                // Recorded on the connector's existing failure-log/event channel, NOT rethrown:
                // rethrowing reopens exactly what the paragraph above closed. Discrimination is on
                // TASK-286's annotation and never on "the table was missing", because ordinary lazy
                // first-touch is also a missing table and is ~245x more common per bring-up.
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
