using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.SchemaDrift;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        /// <summary>
        /// Adds every column <paramref name="type"/> declares and its table lacks — the
        /// <see cref="ColumnDriftKind.Missing"/> half of <see cref="DetectDrift"/>, acted on. Returns the drifts
        /// it closed; empty when there was nothing to add.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-510. Schema-ensure is create-only: <c>CREATE TABLE IF NOT EXISTS</c> never revisits a table
        /// that exists, so a table created before one of its properties had a column mapping (SH-H037) keeps
        /// lacking that column, and every INSERT names a column the table does not have. Measured at consumer
        /// DraCode as two months of lost rows.
        /// </para>
        /// <para>
        /// <b>Additive only, and idempotent.</b> It never drops a column, never retypes one
        /// (<see cref="ColumnDriftKind.TypeMismatch"/> and <see cref="ColumnDriftKind.Unexpected"/> stay
        /// reported, not acted on), and a second call finds nothing missing. An added NOT NULL column is
        /// back-filled with <see cref="Fields.AbstractField.DefaultStoredValue"/>, so existing rows read back as
        /// an entity that never assigned the property would — see <see cref="AbstractConnectorBase.AddColumnDefinition"/>.
        /// </para>
        /// <para>
        /// ⚠ <b>Explicit, and therefore it throws.</b> It is never called from schema-ensure, for the same
        /// reason <see cref="DetectDrift"/> is not (TASK-204/254: nothing that runs on first use may stop a
        /// store starting). A host calls it deliberately — at startup, or from a migration — and a refusal is
        /// a failure it has to see (rule 49: an explicit schema call throws).
        /// </para>
        /// <para>
        /// <b>Refuses, before any DDL</b>, a missing column it cannot add honestly: a primary key or unique
        /// column (one default on many rows violates the constraint, and SQLite refuses both in
        /// <c>ADD COLUMN</c>), an identity column, and a NOT NULL column with no value-type default — a
        /// <c>[Required]</c> string or <c>byte[]</c>, where any back-fill would be a value the framework
        /// invented for a property the model says must be supplied. Those need a migration that knows the
        /// value. The refusal names every such column, and nothing has been altered when it is thrown.
        /// </para>
        /// <para>
        /// A table that does not exist yet is not an error and is left alone: <c>CREATE TABLE</c> will create
        /// it whole on first use.
        /// </para>
        /// </remarks>
        /// <exception cref="NotSupportedException">
        /// The type is not a mapped entity, or this provider has no column catalogue <see cref="DetectDrift"/>
        /// can read — so "nothing missing" could not be told apart from "could not look".
        /// </exception>
        /// <exception cref="InvalidOperationException">A missing column cannot be added; see the remarks.</exception>
        public IReadOnlyList<ColumnDrift> EnsureColumns(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var report = DetectDrift(type);
            if (!report.Supported)
            {
                throw new NotSupportedException(
                    $"Cannot ensure the columns of {type.FullName}: {report.Reason}");
            }
            if (!report.TableExists)
            {
                return Array.Empty<ColumnDrift>();
            }

            var table = DataBase.LoadTable(type);
            var toAdd = new List<(ColumnDrift Drift, Fields.AbstractField Field)>();
            var refused = new List<string>();

            foreach (var drift in report.Drifts.Where(d => d.Kind == ColumnDriftKind.Missing))
            {
                var field = table.Fields.Values.First(f => string.Equals(f.Name, drift.Column, StringComparison.OrdinalIgnoreCase));
                var reason = WhyColumnCannotBeAdded(field);
                if (reason != null)
                {
                    refused.Add($"{drift.Column}: {reason}");
                    continue;
                }
                toAdd.Add((drift, field));
            }

            if (refused.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Table \"{report.Table}\" lacks column(s) that cannot be added to existing rows, so none were added: "
                    + string.Join("; ", refused)
                    + ". Add them with a migration that supplies the value.");
            }

            foreach (var (_, field) in toAdd)
            {
                AlterTableAdd(table.Name, new[] { field });
            }

            return toAdd.Select(x => x.Drift).ToList();
        }

        private static string? WhyColumnCannotBeAdded(Fields.AbstractField field)
        {
            if (field.IsPrimary)
            {
                return "it is a primary key";
            }
            if (field.IsUnique)
            {
                return "it is unique, and every existing row would receive the same value";
            }
            if (field.IsAutoincrement)
            {
                return "it is an identity column";
            }
            if (field.IsNotNull && field.DefaultStoredValue == null)
            {
                return "it is NOT NULL and has no value-type default to give existing rows";
            }
            return null;
        }
    }
}
