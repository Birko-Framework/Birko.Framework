using System;
using System.Data.Common;
using Birko.Data.Migrations.Context;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// Base class for migrations expressed as raw SQL/DDL scripts. A subclass supplies the forward
    /// script via <see cref="UpSql"/> (and optionally the revert script via <see cref="DownSql"/>);
    /// this base runs each against the migration context's connection and active transaction, so
    /// consumers no longer hand-roll the <see cref="IMigrationContext"/> →
    /// <see cref="Context.SqlMigrationContext"/> cast and the connection/command/transaction plumbing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs on the runner's own connection and transaction (default
    /// <c>SqlMigrationSettings.UseTransaction = true</c>), so the DDL and the version-tracking row
    /// commit atomically. Unlike <see cref="CreateTablesMigration"/> (which drives DDL through the
    /// connector's own connection and therefore needs <c>UseTransaction = false</c>), a script
    /// migration is safe under the default transactional runner.
    /// </para>
    /// <para>
    /// A single script may contain multiple statements separated by <c>;</c> where the provider
    /// supports batches — SQLite's <c>ExecuteNonQuery</c> runs a multi-statement batch. Some other
    /// ADO.NET providers execute only the first statement per command; for those, split the work
    /// across migrations or override <see cref="Execute"/>.
    /// </para>
    /// <para>
    /// When <see cref="DownSql"/> is null (the default), <see cref="Down"/> defers to the base
    /// <see cref="Data.Migrations.AbstractMigration.Down"/> — i.e. a <see cref="NotImplementedException"/>.
    /// </para>
    /// </remarks>
    public abstract class SqlScriptMigration : Data.Migrations.AbstractMigration
    {
        /// <summary>The SQL executed when the migration is applied. Required, must be non-empty.</summary>
        protected abstract string UpSql { get; }

        /// <summary>The SQL executed when the migration is reverted. Null (default) means no down script.</summary>
        protected virtual string? DownSql => null;

        /// <inheritdoc />
        public override void Up(IMigrationContext context) => Execute(context, UpSql);

        /// <inheritdoc />
        public override void Down(IMigrationContext context)
        {
            if (DownSql is null)
            {
                base.Down(context);
                return;
            }
            Execute(context, DownSql);
        }

        /// <summary>
        /// Runs a script against the SQL migration context's connection and active transaction
        /// (one <c>ExecuteNonQuery</c>). Override to customize execution (e.g. per-statement batches).
        /// </summary>
        protected virtual void Execute(IMigrationContext context, string sql)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new ArgumentException("Migration SQL must not be null or empty.", nameof(sql));
            }
            if (context is not Context.SqlMigrationContext sqlContext)
            {
                throw new InvalidOperationException(
                    $"{nameof(SqlScriptMigration)} requires a {nameof(Context.SqlMigrationContext)} — run it through {nameof(SqlMigrationRunner)}.");
            }

            using DbCommand command = sqlContext.Connection.CreateCommand();
            command.CommandText = sql;
            if (sqlContext.Transaction != null)
            {
                command.Transaction = sqlContext.Transaction;
            }
            command.ExecuteNonQuery();
        }
    }
}
