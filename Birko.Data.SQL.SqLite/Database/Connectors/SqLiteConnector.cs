using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using SqLiteSettings = Birko.Data.SQL.SqLite.Stores.SqLiteSettings;
using PasswordSettings = Birko.Configuration.PasswordSettings;

namespace Birko.Data.SQL.Connectors
{
    public partial class SqLiteConnector : AbstractAsyncConnector
    {
        public SqLiteConnector(Birko.Configuration.PasswordSettings settings) : base(settings)
        {
            OnException += SqLiteConnector_OnException;
        }

        /// <summary>
        /// Detects SQLite transient errors: database locked (5), database busy (6).
        /// </summary>
        public override bool IsTransientException(Exception ex)
        {
            if (base.IsTransientException(ex)) return true;
            if (ex is SqliteException sqliteEx)
            {
                switch (sqliteEx.SqliteErrorCode)
                {
                    case 5:   // SQLITE_BUSY — database is locked
                    case 6:   // SQLITE_LOCKED — table in the database is locked
                        return true;
                }
            }
            return false;
        }

        /// <remarks>
        /// TASK-277 — this used to answer a missing table with <c>DoInit()</c> and a <b>return</b>, so a
        /// write against a table that does not exist reported success and lost the row. The decision now
        /// lives in <c>AbstractConnector.EnsureSchemaAndReport</c>, shared by all four providers, which
        /// ensures the schema and then reports the failure.
        /// <para>
        /// The typed <c>SqliteException</c> + <c>SqliteErrorCode == 1</c> test that used to be inline is
        /// dropped in favour of <c>IsMissingTableException</c> — SQLite's wording ("no such table") is what
        /// the base classifier already matches, and one classifier means the reader and this handler cannot
        /// disagree about what a missing table is (TASK-211's rule).
        /// </para>
        /// </remarks>
        private void SqLiteConnector_OnException(Exception ex, string? commandText)
            => EnsureSchemaAndReport(ex, commandText);

        public string? Path
        {
            get
            {
                return (!string.IsNullOrEmpty(_settings?.Location) && !string.IsNullOrEmpty(_settings?.Name))
                    ? System.IO.Path.Combine(_settings.Location, _settings.Name)
                    : null;
            }
        }

        // TASK-296 — 0 until the journal mode has been applied for this database, 1 after.
        //
        // Once per CONNECTOR, which is once per database per process: DataBase.GetConnector caches per
        // (type, settings id). That is enough because journal_mode is PERSISTENT IN THE FILE — SQLite
        // stores it in the header, so a later connection inherits it without being told.
        private int _journalModeApplied;

        /// <summary>
        /// The journal mode SQLite reported after this connector tried to apply
        /// <c>SqLiteSettings.JournalMode</c> — or null if it has not run yet, or was switched off.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>Read this rather than assuming the setting took.</b> WAL needs shared memory and does not
        /// engage on most network filesystems or for an in-memory database; SQLite reports the mode that
        /// is actually in force rather than failing, so this is the only way to know. See
        /// <see cref="JournalModeFailure"/> for the case where the attempt threw instead.
        /// </remarks>
        public string? JournalModeInEffect { get; private set; }

        /// <summary>
        /// The exception from applying the journal mode, if it threw. Null in the normal case.
        /// </summary>
        /// <remarks>
        /// Recorded rather than thrown, deliberately, and on the same "swallowed means recorded" terms as
        /// <c>IndexCreationFailures</c> (TASK-204) and <c>SubscriberFailures</c> (TASK-289): a journal mode
        /// is an optimisation and a concurrency property, not the caller's operation, so a database that
        /// cannot take WAL must still be usable. What must not happen is that the failure is invisible.
        /// </remarks>
        public Exception? JournalModeFailure { get; private set; }

        /// <summary>
        /// The journal modes this seam can actually deliver: <c>WAL</c> and <c>DELETE</c>. The value is
        /// interpolated into <c>PRAGMA journal_mode=…</c>, which takes no parameter, so this whitelist
        /// <b>is</b> the containment.
        /// </summary>
        /// <remarks>
        /// <para>
        /// § Conventions' identifier family, at a fourth kind of sink: a bare keyword in statement
        /// position. Nothing quotes it, so escaping contains nothing and refusal is the only mechanism
        /// left — exactly TASK-255's reasoning for a bare column identifier. A closed set is right here
        /// where it was wrong in TASK-260, because the engine's own grammar is closed.
        /// </para>
        /// <para>
        /// ⚠ <b>Two values rather than SQLite's six, and that is a measurement rather than caution.</b>
        /// A journal mode is persistent in the database file <b>only for WAL</b>; the rollback modes are
        /// a <i>per-connection</i> property. Measured (<c>Which_journal_modes_persist_across_connections</c>):
        /// set <c>TRUNCATE</c>, <c>PERSIST</c>, <c>MEMORY</c> or <c>OFF</c> on one connection and a new
        /// connection reports <c>delete</c>; only <c>WAL</c> comes back as itself. This seam applies the
        /// PRAGMA <b>once, on a connection of its own</b>, so accepting those four would accept a value
        /// and then do nothing — the silent-drop shape § SH-H037 exists to forbid. <c>DELETE</c> is kept
        /// because it is meaningful: it takes a database <i>out</i> of WAL, persistently, and is SQLite's
        /// own default otherwise.
        /// </para>
        /// </remarks>
        private static readonly string[] _journalModes = { "WAL", "DELETE" };

        /// <summary>
        /// Applies <c>SqLiteSettings.JournalMode</c> once for this database. Best effort: a mode that
        /// cannot be set leaves the database on whatever it had.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-296, and it is a <b>correctness</b> fix before a performance one. On the rollback journal
        /// a statement on a pooled <c>sqlite3</c> handle can be answered from a schema image older than a
        /// <c>CREATE TABLE</c> another connection has already committed, so a freshly created table reads
        /// as missing — and since TASK-285 a count of a missing table answers <c>0</c>, so it does so
        /// silently. Measured (TASK-290's storm): rollback journal <b>7 of 7</b> runs affected, 2-9
        /// occurrences each; WAL <b>0 of 5</b>.
        /// </para>
        /// <para>
        /// ⚠ <b>Why not <c>Pooling=False</c>, which also fixes it.</b> It is <b>1.5× slower</b> in the warm
        /// sequential case (2,731 ms against 1,801 ms for 200 write+count+read cycles), while WAL is
        /// <b>5× faster</b> (351 ms). The storm made pooling-off look free — 2.4× faster there — which is
        /// a contention artefact and exactly why the remedy was not chosen on a storm number.
        /// </para>
        /// <para>
        /// It runs on a connection of its own rather than on the caller's, because <c>CreateConnection</c>
        /// returns an <b>unopened</b> connection by contract and callers open it themselves. One extra
        /// open per database per process.
        /// </para>
        /// </remarks>
        private void ApplyJournalMode(SqLiteSettings settings)
        {
            if (System.Threading.Interlocked.Exchange(ref _journalModeApplied, 1) != 0)
            {
                return;
            }

            var requested = settings.JournalMode?.Trim();
            if (string.IsNullOrEmpty(requested))
            {
                // The full opt-out: emit no PRAGMA at all and leave the file exactly as it is.
                return;
            }

            var mode = Array.Find(_journalModes,
                m => string.Equals(m, requested, StringComparison.OrdinalIgnoreCase));
            if (mode == null)
            {
                JournalModeFailure = new ArgumentException(
                    $"Unsupported SQLite journal mode '{requested}'. Expected one of: "
                    + string.Join(", ", _journalModes)
                    + ". SQLite persists a journal mode in the database file only for WAL — the rollback "
                    + "modes (TRUNCATE, PERSIST, MEMORY, OFF) are per-connection, and this setting is "
                    + "applied once on a connection of its own, so accepting one would silently do "
                    + "nothing. Set SqLiteSettings.JournalMode to null or empty to leave the database's "
                    + "own journal mode untouched.",
                    nameof(SqLiteSettings.JournalMode));
                return;
            }

            try
            {
                using var db = new SqliteConnection(settings.GetConnectionString());
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=" + mode;
                JournalModeInEffect = Convert.ToString(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                // Best effort. A database that cannot take the requested mode is still usable, and the
                // failure is on the record rather than in the caller's face.
                JournalModeFailure = ex;
            }
        }

        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(Path))
            {
                throw new Exception("No path provided");
            }

            bool init = !System.IO.File.Exists(Path);

            if (settings is SqLiteSettings sqliteSettings)
            {
                ApplyJournalMode(sqliteSettings);
                var connection = new SqliteConnection(sqliteSettings.GetConnectionString());
                if (init)
                {
                    DoInit();
                }
                return connection;
            }

            var connectionString = $"Data Source={Path}";
            if (!string.IsNullOrEmpty(settings.Password))
                connectionString += $";Password={settings.Password}";
            var fallbackConnection = new SqliteConnection(connectionString);
            if (init)
            {
                DoInit();
            }
            return fallbackConnection;
        }

        /// <summary>
        /// TASK-269 — SQLite's column catalogue. <c>pragma_table_info</c> reports the declared type
        /// <b>verbatim</b>, parameters included (<c>DECIMAL(18,2)</c>, not <c>DECIMAL</c>), which is the
        /// whole reason this framework asks a catalogue rather than the reader: measured, a zero-row
        /// <c>SELECT *</c> strips the parameters on this very provider.
        /// </summary>
        /// <remarks>
        /// The table name reaches the statement as a quoted <i>literal</i>, not as an identifier, so it
        /// is escaped with <see cref="SqlLiteral.EscapeLiteral"/> — the sink family § Conventions records
        /// under TASK-253, where the tell is "a quoted literal, not a name".
        /// </remarks>
        protected override string? StoredColumnsSql(string tableName)
            => string.Format("SELECT name, type FROM pragma_table_info('{0}')", SqlLiteral.EscapeLiteral(tableName));

        /// <summary>
        /// Identity: <c>pragma_table_info</c> already speaks <see cref="ConvertType"/>'s vocabulary,
        /// because for a framework-created table the declared type IS what ConvertType emitted.
        /// </summary>
        protected override string RenderStoredType(StoredColumn column) => column.TypeName;

        public override string ConvertType(DbType type, AbstractField field)
        {
            switch (type)
            {
                case DbType.Decimal:
                case DbType.VarNumeric:
                case DbType.Double:
                case DbType.Currency:
                    {
                        if (field is DecimalField decimalField && decimalField.Precision != null && decimalField.Scale != null)
                        {
                            return string.Format("NUMERIC({0},{1})", decimalField.Precision, decimalField.Scale);
                        }
                        else
                        {
                            return "REAL";
                        }
                    }
                case DbType.Boolean:
                case DbType.Date:
                case DbType.DateTime:
                case DbType.Time:
                case DbType.DateTime2:
                case DbType.DateTimeOffset:
                case DbType.Int16:
                case DbType.Int32:
                case DbType.Int64:
                case DbType.UInt16:
                case DbType.UInt32:
                case DbType.UInt64:
                case DbType.SByte:
                case DbType.Byte:
                    return "INTEGER";
                case DbType.Single:
                    // A C# float grouped with the integral types declared an INTEGER column. SQLite's
                    // type affinity masks it for values it cannot losslessly narrow, but the declaration
                    // is still wrong and rounds whole-valued floats to integers. REAL is SQLite's 8-byte
                    // IEEE storage class — the same fix PostgreSQL and MSSql already carry (CR-H087).
                    // Inert until SH-H037 gave `float` a field class; nothing could produce Single before.
                    return "REAL";
                case DbType.Xml:
                case DbType.Object:
                case DbType.Binary:
                    return "BLOB";
                case DbType.Guid:
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                default:
                    return "TEXT";
            }
        }

        public override string FieldDefinition(AbstractField field)
        {
            var result = new StringBuilder();
            if (field != null)
            {
                // TASK-058: SQLite accepts AUTOINCREMENT only as part of an `INTEGER PRIMARY KEY
                // AUTOINCREMENT` column constraint — the keyword is inseparable from an INTEGER primary
                // key, must sit adjacent to PRIMARY KEY, and the declared type must be exactly INTEGER.
                // The previous code appended a bare `AUTOINCREMENT` after any UNIQUE/NOT NULL for ANY
                // autoincrement field, producing invalid DDL: detached from PRIMARY KEY even for a PK
                // column, and — worse — emitted for a NON-primary-key increment field (e.g. a dual-key
                // model with a [PrimaryField] Guid + a separate [IncrementField] Id), which SQLite
                // rejects outright, so CreateTable threw.
                if (field.IsPrimary && field.IsAutoincrement)
                {
                    result.Append(field.Name);
                    result.Append(" INTEGER PRIMARY KEY AUTOINCREMENT");
                    return result.ToString();
                }

                result.Append(field.Name);
                result.AppendFormat(" {0}", ConvertType(field.Type, field));
                if (field.IsPrimary)
                {
                    result.AppendFormat(" PRIMARY KEY");
                }
                // TASK-275 — see AbstractField.UsesInlineUniqueConstraint. Behaviour-preserving here.
                if (field.UsesInlineUniqueConstraint)
                {
                    result.AppendFormat(" UNIQUE");
                }
                if (field.IsNotNull)
                {
                    result.AppendFormat(" NOT NULL");
                }

                // A non-primary-key [IncrementField] cannot be AUTOINCREMENT in SQLite (no per-column
                // auto-increment outside INTEGER PRIMARY KEY), so it is emitted as a plain column and
                // the caller assigns the value. Providers with real non-PK identity (MSSql IDENTITY,
                // PostgreSQL SERIAL) differ here — documented on the connector.
            }
            return result.ToString();
        }

        public override DbCommand AddParameter(DbCommand command, string name, object? value)
        {
            // Enums persist as INTEGER (IntegerField) — bind the underlying integral value rather than
            // relying on the provider's own handling of a boxed enum. See NormalizeParameterValue.
            value = NormalizeParameterValue(value);
            if(value is Guid)
            {
                value = value?.ToString();
            }
            if (command.Parameters.Contains(name))
            {
                ((SqliteParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
            }
            else
            {
                ((SqliteCommand)command).Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
            return command;
        }

        private object? ConvertFieldValue(AbstractField field, object model)
        {
            var value = field.Write(model);
            if (value is Guid guid)
                return guid.ToString();
            return value;
        }

        private object? ConvertPrimaryKeyValue(AbstractField field, object model)
        {
            var value = field.Property.GetValue(model);
            if (value is Guid guid)
                return guid.ToString();
            return value;
        }

        #region Native Bulk Operations

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

            // CR-M144: the own-connection path stays wrapped in ExecuteWithRetry so SQLITE_BUSY/SQLITE_LOCKED
            // (flagged transient by the overridden IsTransientException) are retried per the configured
            // RetryPolicy, matching the base RunCommandTransaction. RunBulk keeps that retry when it owns the
            // connection and skips it when participating in a caller's boundary.
            //
            // A bulk write must JOIN an open boundary on this database rather than open a second connection.
            // The sync store publishes its transaction context into AmbientSqlTransaction exactly as the
            // async one does (DataBaseStore.EnterTransactionScope), so before this the sync single-row writes
            // honoured a boundary while sync create-many / update-many / delete-many escaped it — loudly on
            // SQLite, since the second connection cannot take the write lock the boundary already holds.
            RunBulk("BulkInsert into " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqliteConnection)dbConnection;
                var transaction = (SqliteTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var columnNames = string.Join(", ", fields.Select(f => f.Name));
                    var paramNames = string.Join(", ", fields.Select(f => "@INS_" + f.Name.Replace(".", "")));
                    command.CommandText = "INSERT INTO " + QuoteIdentifier(table.Name)
                        + " (" + columnNames + ") VALUES (" + paramNames + ")";
                    commandText = command.CommandText;

                    foreach (var field in fields)
                    {
                        command.Parameters.AddWithValue("@INS_" + field.Name.Replace(".", ""), DBNull.Value);
                    }

                    foreach (var model in models)
                    {
                        foreach (var field in fields)
                        {
                            command.Parameters["@INS_" + field.Name.Replace(".", "")].Value = ConvertFieldValue(field, model) ?? DBNull.Value;
                        }
                        command.ExecuteNonQuery();
                    }

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkInsert into " + table.Name);
                }
            });
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

            // CR-M144: retry SQLITE_BUSY/SQLITE_LOCKED via ExecuteWithRetryAsync (cancellation still
            // propagates — OperationCanceledException is not transient). RunBulkAsync keeps that retry on
            // the own-connection path and skips it when participating in a caller's boundary.
            //
            // A bulk write must JOIN an open boundary on this database rather than open a second connection:
            // every collection-shaped repository write routes here, so this was create-many / update-many /
            // delete-many / delete-where / delete-all escaping every transaction boundary — loudly on SQLite
            // (the second connection cannot take the write lock the boundary holds) and SILENTLY on
            // PostgreSQL/MySQL (two connections are legal, so it committed and survived the owner's
            // rollback). See RunBulkAsync's remarks.
            await RunBulkAsync("BulkInsertAsync into " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqliteConnection)dbConnection;
                var transaction = (SqliteTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;

                    var columnNames = string.Join(", ", fields.Select(f => f.Name));
                    var paramNames = string.Join(", ", fields.Select(f => "@INS_" + f.Name.Replace(".", "")));
                    command.CommandText = "INSERT INTO " + QuoteIdentifier(table.Name)
                        + " (" + columnNames + ") VALUES (" + paramNames + ")";
                    commandText = command.CommandText;

                    foreach (var field in fields)
                    {
                        command.Parameters.AddWithValue("@INS_" + field.Name.Replace(".", ""), DBNull.Value);
                    }

                    foreach (var model in models)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var field in fields)
                        {
                            command.Parameters["@INS_" + field.Name.Replace(".", "")].Value = ConvertFieldValue(field, model) ?? DBNull.Value;
                        }
                        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    if (owned) transaction.Commit();
                }
                catch (OperationCanceledException)
                {
                    if (owned) transaction.Rollback();
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkInsertAsync into " + table.Name);
                }
            }, ct);
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

            // CR-M144: the own-connection path keeps its ExecuteWithRetry for SQLITE_BUSY/SQLITE_LOCKED;
            // RunBulk skips the retry when participating in a caller's boundary. See BulkInsert above for
            // why a bulk write has to join an open boundary instead of opening a second connection.
            RunBulk("BulkUpdate " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqliteConnection)dbConnection;
                var transaction = (SqliteTransaction)dbTransaction;
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
                        command.Parameters.AddWithValue("@SET_" + field.Name.Replace(".", ""), DBNull.Value);
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.AddWithValue("@PK_" + field.Name.Replace(".", ""), DBNull.Value);
                    }

                    foreach (var model in models)
                    {
                        foreach (var field in updateFields)
                        {
                            command.Parameters["@SET_" + field.Name.Replace(".", "")].Value = ConvertFieldValue(field, model) ?? DBNull.Value;
                        }
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = ConvertPrimaryKeyValue(field, model) ?? DBNull.Value;
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
            });
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

            // CR-M144: retry SQLITE_BUSY/SQLITE_LOCKED via ExecuteWithRetryAsync — kept by RunBulkAsync on
            // the own-connection path, and skipped when participating in a caller's boundary (see its
            // remarks; a bulk write must join an open boundary rather than open a second connection).
            await RunBulkAsync("BulkUpdateAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqliteConnection)dbConnection;
                var transaction = (SqliteTransaction)dbTransaction;
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
                        command.Parameters.AddWithValue("@SET_" + field.Name.Replace(".", ""), DBNull.Value);
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.AddWithValue("@PK_" + field.Name.Replace(".", ""), DBNull.Value);
                    }

                    foreach (var model in models)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var field in updateFields)
                        {
                            command.Parameters["@SET_" + field.Name.Replace(".", "")].Value = ConvertFieldValue(field, model) ?? DBNull.Value;
                        }
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = ConvertPrimaryKeyValue(field, model) ?? DBNull.Value;
                        }
                        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    if (owned) transaction.Commit();
                }
                catch (OperationCanceledException)
                {
                    if (owned) transaction.Rollback();
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkUpdateAsync " + table.Name);
                }
            }, ct);
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

            // CR-M144: the own-connection path keeps its ExecuteWithRetry for SQLITE_BUSY/SQLITE_LOCKED;
            // RunBulk skips the retry when participating in a caller's boundary. See BulkInsert above for
            // why a bulk write has to join an open boundary instead of opening a second connection.
            RunBulk("BulkDelete " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqliteConnection)dbConnection;
                var transaction = (SqliteTransaction)dbTransaction;
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
                        command.Parameters.AddWithValue("@PK_" + field.Name.Replace(".", ""), DBNull.Value);
                    }

                    foreach (var model in models)
                    {
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = ConvertPrimaryKeyValue(field, model) ?? DBNull.Value;
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
            });
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

            // CR-M144: retry SQLITE_BUSY/SQLITE_LOCKED via ExecuteWithRetryAsync — kept by RunBulkAsync on
            // the own-connection path, and skipped when participating in a caller's boundary (see its
            // remarks; a bulk write must join an open boundary rather than open a second connection).
            await RunBulkAsync("BulkDeleteAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqliteConnection)dbConnection;
                var transaction = (SqliteTransaction)dbTransaction;
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
                        command.Parameters.AddWithValue("@PK_" + field.Name.Replace(".", ""), DBNull.Value);
                    }

                    foreach (var model in models)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var field in primaryFields)
                        {
                            command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = ConvertPrimaryKeyValue(field, model) ?? DBNull.Value;
                        }
                        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    if (owned) transaction.Commit();
                }
                catch (OperationCanceledException)
                {
                    if (owned) transaction.Rollback();
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkDeleteAsync " + table.Name);
                }
            }, ct);
        }

        #endregion
    }
}
