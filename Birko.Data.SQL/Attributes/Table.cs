using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.SQL.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public class Table : System.Attribute
    {
        public string Name { get; private set; }

        /// <summary>
        /// The schema the table lives in, or null for the connection's default (TASK-272). On PostgreSQL, SQL Server and
        /// TimescaleDB the table is <c>schema.name</c>; on MySQL and SQLite, which have no schema inside a database, it is
        /// the table <c>schema__name</c> in the same database. A plain identifier: letters, digits and underscores.
        /// </summary>
        public string? Schema { get; set; }

        public Table(string name)
        {
            Name = name;
        }
    }
}
