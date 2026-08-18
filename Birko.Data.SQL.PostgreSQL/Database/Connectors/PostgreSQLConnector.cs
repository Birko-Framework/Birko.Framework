using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.Stores;
using PostgreSqlSettings = Birko.Data.SQL.PostgreSQL.Stores.PostgreSqlSettings;
using Npgsql;
using NpgsqlTypes;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// PostgreSQL database connector.
    /// </summary>
    public partial class PostgreSQLConnector : AbstractAsyncConnector
    {
        /// <summary>
        /// Initializes a new instance of the PostgreSQLConnector class.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        public PostgreSQLConnector(RemoteSettings settings) : base(settings)
        {
            OnException += PostgreSQLConnector_OnException;
        }

        /// <summary>
        /// Detects PostgreSQL transient errors: deadlocks (40P01), serialization failures (40001),
        /// connection exceptions (08xxx), insufficient resources (53xxx), operator intervention (57xxx).
        /// </summary>
        public override bool IsTransientException(Exception ex)
        {
            if (base.IsTransientException(ex)) return true;
            // PostgresException derives from NpgsqlException, so a single pattern-test suffices (CR-L188).
            if (ex is PostgresException pgEx)
            {
                var code = pgEx.SqlState;
                if (code != null)
                {
                    // Class 08 — Connection Exception
                    if (code.StartsWith("08")) return true;
                    // Class 40 — Transaction Rollback (deadlock, serialization failure)
                    if (code.StartsWith("40")) return true;
                    // Class 53 — Insufficient Resources (disk full, out of memory, too many connections)
                    if (code.StartsWith("53")) return true;
                    // Class 57 — Operator Intervention (crash recovery, cannot connect now)
                    if (code.StartsWith("57")) return true;
                }
            }
            // Npgsql wrapper exceptions (broken connection)
            if (ex is NpgsqlException && ex.InnerException is System.IO.IOException) return true;
            return false;
        }

        /// <summary>
        /// PostgreSQL phrases a missing table/relation as 'relation "x" does not exist' (SQLSTATE 42P01).
        /// Adds that to the base SQLite match so the reader yields an empty result rather than faulting.
        /// <para>
        /// <b>TASK-211 narrowed this on both axes, because a reader that answers "no rows" to an error is
        /// a wrong answer, not a degraded one.</b> It used to accept any <c>42P01</c> plus a bare
        /// <c>Message.Contains("does not exist")</c>. Both are wider than the name:
        /// </para>
        /// <list type="bullet">
        /// <item><c>42P01</c> (<i>undefined_table</i>) is also what PostgreSQL raises for
        /// <c>missing FROM-clause entry for table "x"</c> — an error about the STATEMENT, where the relation
        /// exists perfectly well. That is the exact error the framework's own qualifier defect produced, so
        /// the swallow hid the bug that produced it.</item>
        /// <item>the message catch-all additionally covered <c>42703</c> undefined <b>column</b>,
        /// <c>42883</c> undefined function and <c>42704</c> undefined object. Measured on 16.4:
        /// <c>SELECT NoSuchColumn FROM "OfPersons"</c> returned an empty result with no exception.</item>
        /// </list>
        /// <para>
        /// Now: the SQLSTATE is the primary key, and the message is consulted <b>only</b> to separate the two
        /// shapes that share <c>42P01</c>. The one case this is entitled to swallow — a relation that
        /// genuinely does not exist — still does, which the lazy create-on-first-use path and view-existence
        /// probing (CR-M149) depend on. The untyped fallback is kept for an exception that reaches here
        /// carrying only the wording (nothing in the framework produces one — <c>InitException</c> wraps and
        /// preserves the inner <see cref="PostgresException"/> — but it is a shipped contract with tests on
        /// it), narrowed from "does not exist" to PostgreSQL's <b>relation</b> phrasing, which is what makes
        /// it a missing-table signal rather than a missing-anything one.
        /// </para>
        /// </summary>
        public override bool IsMissingTableException(Exception ex)
        {
            if (base.IsMissingTableException(ex)) return true;

            var pgEx = FindPostgresException(ex);
            if (pgEx != null)
            {
                // The SQLSTATE decides, and it EXCLUDES rather than includes on the message. Keying the
                // positive case on English text would break lazy create-on-first-use against a server whose
                // `lc_messages` is not English — PostgreSQL localizes these — turning a missing table into a
                // thrown exception there and nowhere else. Excluding on text degrades the other way: on a
                // localized server the missing-FROM-clause shape is swallowed again, which is where this
                // started but is now only defence in depth, since the builders no longer emit it (TASK-211).
                if (pgEx.SqlState != "42P01") return false;
                return !pgEx.MessageText.Contains("missing FROM-clause entry", StringComparison.OrdinalIgnoreCase);
            }

            // 'relation "x" does not exist' — the missing-TABLE wording. A missing column reads
            // 'column "x" does not exist' and a missing function 'function x(...) does not exist', so
            // requiring "relation" is what separates the error this may swallow from the ones it may not.
            return ex.Message.Contains("relation", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The <see cref="PostgresException"/> in an exception chain, if any. Npgsql wraps in some paths, and
        /// the message-substring test this replaced matched a wrapped exception by accident; walking the
        /// chain keeps that reachability without the false positives.
        /// </summary>
        private static PostgresException? FindPostgresException(Exception? ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is PostgresException pgEx) return pgEx;
            }
            return null;
        }

        private void PostgreSQLConnector_OnException(Exception ex, string? commandText)
        {
            // TASK-211: the same narrowing, and for the same reason. This handler swallowed ANY message
            // containing "does not exist" — it called DoInit() and RETURNED, so the caller was told the
            // statement had succeeded. That is what let `CreateView` report success while creating nothing
            // (measured by TASK-209, whose first regression test passed against the unfixed code because of
            // it). Only a genuinely missing relation is a reason to run the lazy init and continue.
            if (!IsInitializing && IsMissingTableException(ex))
            {
                DoInit();
            }
            else
            {
                throw new Exception(commandText, ex);
            }
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Location) || string.IsNullOrEmpty(settings.Name))
            {
                throw new Exception("Invalid settings provided for PostgreSQL connection");
            }

            if (settings is PostgreSqlSettings pgSettings)
            {
                return new NpgsqlConnection(pgSettings.GetConnectionString());
            }

            if (settings is RemoteSettings remoteSettings)
            {
                var port = remoteSettings.Port > 0 ? remoteSettings.Port : 5432;
                var connectionString = string.Format("Host={0};Port={1};Username={2};Password={3};Database={4}",
                    remoteSettings.Location,
                    port,
                    remoteSettings.UserName,
                    remoteSettings.Password,
                    remoteSettings.Name);
                if (remoteSettings.UseSecure)
                {
                    connectionString += ";SSL Mode=Require";
                }
                return new NpgsqlConnection(connectionString);
            }

            throw new Exception("Invalid settings provided for PostgreSQL connection");
        }

        /// <inheritdoc />
        public override string ConvertType(DbType type, AbstractField field)
        {
            switch (type)
            {
                case DbType.VarNumeric:
                case DbType.Decimal:
                    if (field is DecimalField decimalField && decimalField.Precision != null && decimalField.Scale != null)
                    {
                        return string.Format("NUMERIC({0},{1})", decimalField.Precision, decimalField.Scale);
                    }
                    else
                    {
                        return "NUMERIC";
                    }
                case DbType.Double:
                    return "DOUBLE PRECISION";
                case DbType.Currency:
                    return "MONEY";
                case DbType.Boolean:
                    return "BOOLEAN";
                case DbType.Time:
                    return "TIME";
                case DbType.Date:
                    return "DATE";
                case DbType.DateTime:
                case DbType.DateTime2:
                    return "TIMESTAMP";
                case DbType.DateTimeOffset:
                    return "TIMESTAMPTZ";
                case DbType.Int16:
                case DbType.UInt16:
                    return "SMALLINT";
                case DbType.UInt32:
                case DbType.Int32:
                    return "INTEGER";
                case DbType.Int64:
                case DbType.UInt64:
                    return "BIGINT";
                case DbType.Single:
                    // A C# float grouped with SByte/Byte produced a SMALLINT (integer) column,
                    // truncating the value and dropping fractions. REAL is PostgreSQL's 4-byte
                    // single-precision float (same class of bug as MSSql CR-H087).
                    return "REAL";
                case DbType.SByte:
                case DbType.Byte:
                    return "SMALLINT";
                case DbType.Object:
                case DbType.Binary:
                    return "BYTEA";
                case DbType.Guid:
                    return "UUID";
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                default:
                    if (field is CharField charField)
                    {
                        return string.Format("VARCHAR({0})", charField.Lenght);
                    }
                    else
                    {
                        return "TEXT";
                    }
            }
        }

        /// <inheritdoc />
        public override string FieldDefinition(AbstractField field)
        {
            var result = new StringBuilder();
            if (field != null)
            {
                result.Append(field.Name);

                // CR-M142: emit the SERIAL type directly rather than String.Replace-ing the composed
                // definition (brittle string surgery on the whole field text). Choose the auto-increment
                // pseudo-type at emit time.
                string sqlType;
                if (field.IsAutoincrement && (field.Type == DbType.Int64 || field.Type == DbType.UInt64))
                {
                    sqlType = "BIGSERIAL";
                }
                else if (field.IsAutoincrement && (field.Type == DbType.Int32 || field.Type == DbType.UInt32))
                {
                    sqlType = "SERIAL";
                }
                else if (field.IsAutoincrement && (field.Type == DbType.Int16 || field.Type == DbType.UInt16))
                {
                    sqlType = "SMALLSERIAL";
                }
                else
                {
                    sqlType = ConvertType(field.Type, field);
                }
                result.AppendFormat(" {0}", sqlType);

                if (field.IsPrimary)
                {
                    result.AppendFormat(" PRIMARY KEY");
                }
                if (field.IsUnique && !field.IsPrimary)
                {
                    result.AppendFormat(" UNIQUE");
                }
                if (field.IsNotNull)
                {
                    result.AppendFormat(" NOT NULL");
                }
            }
            return result.ToString();
        }

        /// <inheritdoc />
        public override DbCommand AddParameter(DbCommand command, string name, object? value)
        {
            // Enums persist as INTEGER (IntegerField) — bind the underlying integral value, never the
            // boxed enum, or the provider maps it to its own type and the comparison never matches.
            value = NormalizeParameterValue(value);
            if (command.Parameters.Contains(name))
            {
                ((NpgsqlParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
            }
            else
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            return command;
        }

        /// <inheritdoc />
        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            // DoDdlCommand, not DoCommand: on a provider whose DDL is not transactional this must not run
            // on an ambient boundary's connection, because the statement would implicitly commit it
            // (TASK-243). inOwnTransaction: false keeps this emitter autocommitted exactly as it was.
            DoDdlCommand((command) =>
            {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS "
                    + QuoteIdentifier(name)
                    + " ("
                    + string.Join(", ", fields.Where(x => !string.IsNullOrEmpty(x)))
                    + ")";
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true, inOwnTransaction: false);
        }

        #region Native Bulk Operations

        private static NpgsqlDbType DbTypeToNpgsqlDbType(DbType dbType)
        {
            return dbType switch
            {
                DbType.Boolean => NpgsqlDbType.Boolean,
                DbType.Byte or DbType.SByte => NpgsqlDbType.Smallint,
                DbType.Single => NpgsqlDbType.Real,
                DbType.Int16 or DbType.UInt16 => NpgsqlDbType.Smallint,
                DbType.Int32 or DbType.UInt32 => NpgsqlDbType.Integer,
                DbType.Int64 or DbType.UInt64 => NpgsqlDbType.Bigint,
                DbType.Decimal or DbType.VarNumeric or DbType.Currency => NpgsqlDbType.Numeric,
                DbType.Double => NpgsqlDbType.Double,
                DbType.Guid => NpgsqlDbType.Uuid,
                DbType.Date => NpgsqlDbType.Date,
                DbType.Time => NpgsqlDbType.Time,
                DbType.DateTime or DbType.DateTime2 => NpgsqlDbType.Timestamp,
                DbType.DateTimeOffset => NpgsqlDbType.TimestampTz,
                DbType.Binary or DbType.Object => NpgsqlDbType.Bytea,
                _ => NpgsqlDbType.Text,
            };
        }

        public void BulkInsert(Type type, IEnumerable<object> models)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var fields = table.Fields.Select(f => f.Value).Where(f => !f.IsAutoincrement).ToList();
            if (!fields.Any())
                return;

            // Bare, not quoted — the sixth instance of the identifier family in § Conventions, and it means
            // this COPY has NEVER worked for a PascalCase-named column. CreateTable quotes the table name and
            // emits column definitions BARE, so on PostgreSQL every base column is stored case-folded
            // ("Name" -> name) while the table keeps its case. A quoted "Name" in the COPY column list
            // therefore cannot resolve: measured against 16, `COPY "T" ("Name") FROM STDIN` is
            // 42703 column "Name" of relation "T" does not exist. Found by this task's own regression test,
            // which could not otherwise reach the boundary behaviour it is here to prove.
            //
            // The reserved-word objection does not apply: a column needing quotes could not have had its
            // table created in the first place (CREATE TABLE "T" (Order text) is already a syntax error), so
            // there is no working case to break. MySQL's identical spelling is left alone deliberately —
            // column names there are case-insensitive, so nothing is broken and changing it is risk for
            // nothing.
            var columnList = string.Join(", ", fields.Select(f => f.Name));
            var copyCommand = "COPY " + QuoteIdentifier(table.Name)
                + " (" + columnList + ") FROM STDIN (FORMAT BINARY)";

            // A bulk write must JOIN an open boundary on this database rather than open a second connection.
            // On PostgreSQL two connections are perfectly legal, so before this the COPY committed
            // independently and SURVIVED the owner's rollback — no error anywhere, which is the dangerous
            // half of the defect (SQLite at least blocked and failed loudly). Npgsql supports a binary
            // import inside an already-open transaction, so participating needs nothing but the boundary's
            // connection, and the boundary's commit is what makes the rows durable.
            //
            // RunBulkOnConnection rather than RunBulk: COPY carries its own atomicity and ran unwrapped
            // here, so the owned path is left exactly as it was — a connection and no transaction.
            // retryWhenOwned: false for the same reason — this path never retried.
            RunBulkOnConnection(copyCommand, (dbConnection, _, owned) =>
            {
                var connection = (NpgsqlConnection)dbConnection;
                try
                {
                    using var writer = connection.BeginBinaryImport(copyCommand);
                    foreach (var model in models)
                    {
                        writer.StartRow();
                        foreach (var field in fields)
                        {
                            var value = field.Write(model);
                            if (value == null)
                            {
                                writer.WriteNull();
                            }
                            else
                            {
                                writer.Write(value, DbTypeToNpgsqlDbType(field.Type));
                            }
                        }
                    }
                    writer.Complete();
                }
                catch (Exception ex)
                {
                    InitException(ex, copyCommand);
                }
            }, retryWhenOwned: false);
        }

        public async Task BulkInsertAsync(Type type, IEnumerable<object> models, CancellationToken ct = default)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var fields = table.Fields.Select(f => f.Value).Where(f => !f.IsAutoincrement).ToList();
            if (!fields.Any())
                return;

            // Bare, not quoted — see BulkInsert above: a quoted column cannot resolve against the
            // case-folded identifier the bare DDL created.
            var columnList = string.Join(", ", fields.Select(f => f.Name));
            var copyCommand = "COPY " + QuoteIdentifier(table.Name)
                + " (" + columnList + ") FROM STDIN (FORMAT BINARY)";

            // See BulkInsert above: the COPY joins an open boundary instead of opening a second connection,
            // which on PostgreSQL is what let a bulk insert survive the owner's rollback silently.
            await RunBulkOnConnectionAsync(copyCommand, async (dbConnection, _, owned) =>
            {
                var connection = (NpgsqlConnection)dbConnection;
                try
                {
                    await using var writer = await connection.BeginBinaryImportAsync(copyCommand, ct).ConfigureAwait(false);
                    foreach (var model in models)
                    {
                        ct.ThrowIfCancellationRequested();
                        await writer.StartRowAsync(ct).ConfigureAwait(false);
                        foreach (var field in fields)
                        {
                            var value = field.Write(model);
                            if (value == null)
                            {
                                await writer.WriteNullAsync(ct).ConfigureAwait(false);
                            }
                            else
                            {
                                await writer.WriteAsync(value, DbTypeToNpgsqlDbType(field.Type), ct).ConfigureAwait(false);
                            }
                        }
                    }
                    await writer.CompleteAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    InitException(ex, copyCommand);
                }
            }, ct, retryWhenOwned: false);
        }

        public void BulkUpdate(Type type, IEnumerable<object> models)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            var allFields = table.Fields.Select(f => f.Value).ToList();
            var updateFields = allFields.Where(f => !f.IsPrimary && !f.IsAutoincrement).ToList();
            if (!updateFields.Any())
                return;

            // A bulk write must JOIN an open boundary on this database rather than open a second connection.
            // On PostgreSQL two connections are perfectly legal, so before this the statements committed on
            // their own transaction and SURVIVED the owner's rollback with no error anywhere — the quiet
            // half of the defect. retryWhenOwned: false keeps the own-connection path exactly as it shipped;
            // this path never retried and the fix is not the place to start.
            RunBulk("BulkUpdate " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (NpgsqlConnection)dbConnection;
                var transaction = (NpgsqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var setClauses = updateFields.Select(f => f.Name + " = @SET_" + f.Name.Replace(".", ""));
                    var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                    command.CommandText = "UPDATE " + QuoteIdentifier(table.Name)
                        + " SET " + string.Join(", ", setClauses)
                        + " WHERE " + string.Join(" AND ", whereClauses);
                    commandText = command.CommandText;

                    foreach (var field in updateFields)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    command.Prepare();

                    foreach (var model in models)
                    {
                        foreach (var field in updateFields)
                        {
                            command.Parameters["@SET_" + field.Name.Replace(".", "")].Value = field.Write(model) ?? DBNull.Value;
                        }
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                        }
                        command.ExecuteNonQuery();
                    }

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkUpdate " + table.Name);
                }
            }, retryWhenOwned: false);
        }

        public async Task BulkUpdateAsync(Type type, IEnumerable<object> models, CancellationToken ct = default)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            var allFields = table.Fields.Select(f => f.Value).ToList();
            var updateFields = allFields.Where(f => !f.IsPrimary && !f.IsAutoincrement).ToList();
            if (!updateFields.Any())
                return;

            // Joins an open boundary instead of opening a second connection — see BulkUpdate above.
            await RunBulkAsync("BulkUpdateAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (NpgsqlConnection)dbConnection;
                var transaction = (NpgsqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var setClauses = updateFields.Select(f => f.Name + " = @SET_" + f.Name.Replace(".", ""));
                    var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                    command.CommandText = "UPDATE " + QuoteIdentifier(table.Name)
                        + " SET " + string.Join(", ", setClauses)
                        + " WHERE " + string.Join(" AND ", whereClauses);
                    commandText = command.CommandText;

                    foreach (var field in updateFields)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    await command.PrepareAsync(ct).ConfigureAwait(false);

                    foreach (var model in models)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var field in updateFields)
                        {
                            command.Parameters["@SET_" + field.Name.Replace(".", "")].Value = field.Write(model) ?? DBNull.Value;
                        }
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                        }
                        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    if (owned) await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    InitException(ex, commandText ?? "BulkUpdateAsync " + table.Name);
                }
            }, ct, retryWhenOwned: false);
        }

        public void BulkDelete(Type type, IEnumerable<object> models)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            // Joins an open boundary instead of opening a second connection — see BulkUpdate above.
            RunBulk("BulkDelete " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (NpgsqlConnection)dbConnection;
                var transaction = (NpgsqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                    command.CommandText = "DELETE FROM " + QuoteIdentifier(table.Name)
                        + " WHERE " + string.Join(" AND ", whereClauses);
                    commandText = command.CommandText;

                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    command.Prepare();

                    foreach (var model in models)
                    {
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                        }
                        command.ExecuteNonQuery();
                    }

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkDelete " + table.Name);
                }
            }, retryWhenOwned: false);
        }

        public async Task BulkDeleteAsync(Type type, IEnumerable<object> models, CancellationToken ct = default)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            // Joins an open boundary instead of opening a second connection — see BulkUpdate above.
            await RunBulkAsync("BulkDeleteAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (NpgsqlConnection)dbConnection;
                var transaction = (NpgsqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                    command.CommandText = "DELETE FROM " + QuoteIdentifier(table.Name)
                        + " WHERE " + string.Join(" AND ", whereClauses);
                    commandText = command.CommandText;

                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    await command.PrepareAsync(ct).ConfigureAwait(false);

                    foreach (var model in models)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                        }
                        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    if (owned) await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    InitException(ex, commandText ?? "BulkDeleteAsync " + table.Name);
                }
            }, ct, retryWhenOwned: false);
        }

        #endregion
    }
}
