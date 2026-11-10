using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        public void Insert(object model)
        {
            if (model != null)
            {
                Insert(model.GetType(), model);
            }
        }

        public void Insert(Type type, object model)
        {
            Insert(LoadUsableTable(type), model);
        }

        public void Insert(Tables.Table table, object model)
        {
            RequireUsableTable(table); // TASK-891
            if (model != null)
            {
                Insert(table, new[] { DataBase.Write(table.Fields.Select(f => f.Value), model) });
            }
        }

        public void Insert(Tables.Table table, IDictionary<string, object> values)
        {
            RequireUsableTable(table); // TASK-891
            var tableName = table.Name;
            Insert(tableName, values);
        }

        public void Insert(Tables.Table table, IEnumerable<object> models)
        {
            RequireUsableTable(table); // TASK-891
            if (models != null && models.Any() && models.Any(x => x != null))
            {
                var tableName = table.Name;
                Insert(tableName, models.Where(x => x != null).Select(x => DataBase.Write(table.Fields.Select(f => f.Value), x)));
            }
        }

        private void Insert(string tableName, IDictionary<string, object> values)
        {
            Insert(tableName, new[] { values });
        }

        public void Insert(Tables.Table table, IEnumerable<IDictionary<string, object>> values)
        {
            RequireUsableTable(table); // TASK-891
            var tableName = table.Name;
            Insert(tableName, values);
        }

        public void Insert(string tableName, IEnumerable<IDictionary<string, object>> values)
        {
            if (values == null) return;
            // Materialize once: values is enumerated for the guard, the first-row schema, and the
            // per-row bind, and a one-shot/lazy source would otherwise be walked repeatedly.
            var rows = values as IReadOnlyList<IDictionary<string, object>> ?? values.ToList();
            if (rows.Count == 0 || !rows.All(x => x.Any())) return;

            var first = rows[0];
            // The INSERT column list and parameter names are built from the first row, and every row
            // rebinds parameters by key; a row with a different key set would silently mis-bind (a
            // missing key leaves the prior row's stale value, an extra key binds an unused parameter).
            // Require key-set equality rather than mis-binding heterogeneous dictionaries. (CR-L174)
            var expectedKeys = new HashSet<string>(first.Keys);
            foreach (var row in rows)
            {
                if (!expectedKeys.SetEquals(row.Keys))
                {
                    throw new ArgumentException(
                        "All rows in a bulk insert must have the same column set as the first row.",
                        nameof(values));
                }
            }
            // TASK-891: a column the provider rejects is refused by name, before the transaction wrapper re-wraps it.
            RequireUsableStatementColumns(new[] { tableName }, first.Keys, null);

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = "INSERT INTO " + TableIdentifier(tableName)
                            + " (" + string.Join(", ", first.Keys) + ")"
                            + " VALUES"
                            + " (" + string.Join(", ", first.Keys.Select(x => "@" + x.Replace(".", string.Empty))) + ")";
                foreach (var kvp in first)
                {
                    AddParameter(command, "@" + kvp.Key.Replace(".", string.Empty), kvp.Value);
                }
            }, (command) =>
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    foreach (var kvp in rows[i])
                    {
                        AddParameter(command, "@" + kvp.Key.Replace(".", string.Empty), kvp.Value);
                    }
                    // TASK-875: the runner announced the first row; every further row is a statement sent too.
                    if (i > 0) AnnounceCommand(command);
                    command.ExecuteNonQuery();
                }
            }, true);
        }
    }
}
