using System.Collections.Generic;

namespace Birko.Data.SQL.SchemaDrift
{
    /// <summary>
    /// The statements that repair one table's drift, and the drifts they deliberately leave to an operator.
    /// </summary>
    /// <remarks>
    /// TASK-633. <see cref="Connectors.AbstractConnector.DetectDrift"/> reports; this turns the report into SQL a host can
    /// review, rehearse on a copy and apply. The framework never runs it: when to change a production schema is the
    /// consumer's decision. Only the two repairs that cannot lose data are written: a missing column is added (with the
    /// back-fill its existing rows need), and a column stored under another case is renamed to the spelling the connector
    /// reads (adding that one instead would orphan the stored values).
    /// </remarks>
    public sealed class SchemaRepairPlan
    {
        public SchemaRepairPlan(SchemaDriftReport report, IReadOnlyList<string> statements, IReadOnlyList<SchemaRepairSkip> notActioned,
            IReadOnlyList<string>? requirements = null)
        {
            Report = report;
            Statements = statements;
            NotActioned = notActioned;
            Requirements = requirements ?? System.Array.Empty<string>();
        }

        /// <summary>
        /// What the connection that runs <see cref="Statements"/> must provide, when it is not just any connection to the
        /// database (rule 64): on SQLite, a decimal column names a collation only a registered connection has.
        /// </summary>
        public IReadOnlyList<string> Requirements { get; }

        /// <summary>The drift report the plan was made from.</summary>
        public SchemaDriftReport Report { get; }

        /// <summary>The table the plan repairs.</summary>
        public string Table => Report.Table;

        /// <summary>The statements, in the order they must run. Empty when there is nothing the plan may repair.</summary>
        public IReadOnlyList<string> Statements { get; }

        /// <summary>Every drift the plan does not repair, each with the reason it is the operator's.</summary>
        public IReadOnlyList<SchemaRepairSkip> NotActioned { get; }

        /// <summary>True when the statements repair every drift: nothing is left to an operator.</summary>
        public bool IsComplete => NotActioned.Count == 0;
    }

    /// <summary>A drift the repair plan leaves to an operator, and why.</summary>
    public sealed class SchemaRepairSkip
    {
        public SchemaRepairSkip(string drift, string reason)
        {
            Drift = drift;
            Reason = reason;
        }

        /// <summary>The drift, as the report describes it.</summary>
        public string Drift { get; }

        /// <summary>Why no statement is written for it.</summary>
        public string Reason { get; }

        public override string ToString() => Drift + " (" + Reason + ")";
    }
}
