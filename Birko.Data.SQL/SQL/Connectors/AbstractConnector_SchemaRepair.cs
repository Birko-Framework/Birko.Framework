using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.SchemaDrift;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        /// <summary>
        /// The SQL that repairs <paramref name="type"/>'s table drift, without running it: an <c>ADD</c> per missing
        /// column and a rename per column stored under another case. Every other drift is listed in
        /// <see cref="SchemaRepairPlan.NotActioned"/> with the reason.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-633, for a consumer that reviews and rehearses schema changes before production (Symbio's deploy applies
        /// them to a restored copy first). It is the statement form of <see cref="EnsureColumns"/>, built from the same
        /// producers so the two cannot disagree:
        /// </para>
        /// <list type="bullet">
        /// <item><b>Missing</b>: the <c>ALTER TABLE … ADD</c> <see cref="EnsureColumns"/> runs, typed by
        /// <see cref="AbstractConnectorBase.ConvertType"/> (the source <c>CREATE TABLE</c> uses). A NOT NULL column carries
        /// a <c>DEFAULT</c> of its type's default, which is what back-fills the existing rows: a column added without it
        /// would leave them NULL, and a non-nullable property's reader (<c>GetGuid</c>, <c>GetInt32</c>) throws on every
        /// read of the entity, measured at Symbio. A column <see cref="EnsureColumns"/> refuses (a key, a unique column,
        /// an identity, a NOT NULL column with no value-type default) is listed instead: any value for it would be
        /// invented.</item>
        /// <item><b>CaseMismatch</b> (a provider resolving identifiers by case, TASK-610 / TASK-611): a rename from the
        /// stored spelling to <see cref="AbstractConnectorBase.StoredColumnName"/>, the one the connector reads, which
        /// keeps the values.</item>
        /// <item><b>Not written</b>: a type, nullability or key change can lose data or fail on existing rows, an
        /// <c>Unexpected</c> column may hold data no model maps, and an index change can fail on existing duplicates.</item>
        /// </list>
        /// <para>Explicit, so it throws where it cannot look (rule 49), as <see cref="EnsureColumns"/> does. A table that
        /// does not exist yet gets an empty plan: <c>CREATE TABLE</c> makes it whole on first use.</para>
        /// </remarks>
        /// <exception cref="NotSupportedException">The type is not a mapped entity, or the provider has no column catalogue.</exception>
        public SchemaRepairPlan PlanRepair(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var report = DetectDrift(type);
            if (!report.Supported)
            {
                throw new NotSupportedException($"Cannot plan a repair of {type.FullName}: {report.Reason}");
            }

            var statements = new List<string>();
            var skipped = new List<SchemaRepairSkip>();
            if (!report.TableExists)
            {
                return new SchemaRepairPlan(report, statements, skipped);
            }

            var table = DataBase.LoadTable(type);
            foreach (var drift in report.Drifts)
            {
                switch (drift.Kind)
                {
                    case ColumnDriftKind.Missing:
                        var field = table.Fields.Values.First(f => f.Name == drift.Column);
                        var refusal = WhyColumnCannotBeAdded(field);
                        if (refusal != null)
                        {
                            skipped.Add(new SchemaRepairSkip(drift.ToString(), refusal + "; add it with a migration that supplies the value"));
                            break;
                        }
                        statements.Add(AddColumnSql(table.Name, field));
                        break;

                    case ColumnDriftKind.CaseMismatch:
                        statements.Add(RenameColumnSql(table.Name, drift.StoredColumn!, StoredColumnName(drift.Column)));
                        break;

                    default:
                        skipped.Add(new SchemaRepairSkip(drift.ToString(), WhyNotRepaired(drift.Kind)));
                        break;
                }
            }

            foreach (var index in report.IndexDrifts)
            {
                skipped.Add(new SchemaRepairSkip(index.ToString(),
                    "an index change can fail on existing rows (a unique index over duplicates) and is the operator's"));
            }

            var requirements = statements.Select(StatementRequirement).Where(r => r != null).Select(r => r!).Distinct().ToList();
            return new SchemaRepairPlan(report, statements, skipped, requirements);
        }

        private static string WhyNotRepaired(ColumnDriftKind kind) => kind switch
        {
            ColumnDriftKind.TypeMismatch => "a type change can lose data or fail on existing values",
            ColumnDriftKind.NullabilityMismatch => "making a column NOT NULL fails on existing NULLs, and the reverse changes what the model may read",
            ColumnDriftKind.KeyMismatch => "a key change fails on duplicate values and rebuilds the table on some providers",
            ColumnDriftKind.Unexpected => "the column may hold data no model maps; dropping it is the operator's decision",
            _ => "no repair is defined for this drift",
        };
    }
}
