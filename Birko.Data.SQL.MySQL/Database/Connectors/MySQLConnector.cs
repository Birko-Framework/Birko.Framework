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
using MySqlSettings = Birko.Data.SQL.MySQL.Stores.MySqlSettings;
using MySqlConnector;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// MySQL database connector.
    /// </summary>
    public partial class MySQLConnector : AbstractAsyncConnector
    {
        private const int DefaultBulkInsertBatchSize = 1000;

        /// <summary>
        /// Initializes a new instance of the MySQLConnector class.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        public MySQLConnector(RemoteSettings settings) : base(settings)
        {
            OnException += MySQLConnector_OnException;
        }

        /// <summary>
        /// Detects MySQL transient errors: too many connections (1040), lock wait timeout (1205),
        /// deadlock (1213), query interrupted (1317), can't-connect (2002/2003), server gone (2006),
        /// lost connection (2013).
        /// </summary>
        /// <summary>
        /// <b>False.</b> MySQL implicitly commits an open transaction before and after every DDL
        /// statement, so DDL issued on a caller's connection destroys their transaction rather than
        /// joining it.
        /// </summary>
        /// <remarks>
        /// Measured on MySQL 8.4 (TASK-243). Stores initialise lazily, so a store's <i>first</i> data
        /// access issues <c>CREATE TABLE IF NOT EXISTS</c> — and after TASK-240 that ran on the ambient
        /// boundary's connection. The boundary was therefore committed before the caller's own write even
        /// ran, and the later rollback undid nothing: three rows survived a rolled-back boundary with no
        /// error anywhere. Silent on the way in (the DDL succeeds) and silent on the way out (the rollback
        /// reports success).
        /// <para>
        /// Returning false makes <c>AbstractConnector.DoDdlCommand</c> issue schema DDL with the boundary
        /// suppressed, on a connection of its own. That is safe here for the same reason the defect exists
        /// here: MySQL permits the second connection. Measured, rather than assumed — an open transaction
        /// holding a row lock on a table does not block a concurrent
        /// <c>CREATE TABLE IF NOT EXISTS</c> on that same table (17 ms), so this is not a metadata-lock
        /// hazard. The created table is not rolled back with the caller's transaction, which is the
        /// intended outcome: schema is not part of the caller's unit of work.
        /// </para>
        /// </remarks>
        public override bool SupportsTransactionalDdl => false;

        /// <summary>
        /// Column length used for an <b>indexed</b> string that declares no explicit length (TASK-248).
        /// </summary>
        /// <remarks>
        /// 255 characters. Under <c>utf8mb4</c> that is 1020 bytes, comfortably inside InnoDB's 3072-byte
        /// index-key limit even in a composite with a <c>CHAR(36)</c> Guid (1164 bytes total), and it is the
        /// conventional bound for the identifier-ish columns that actually get indexed — document numbers,
        /// codes, e-mail addresses. Raise it in a derived connector if a consumer genuinely indexes longer
        /// values, but note the index-key limit is the real ceiling, not this number.
        /// <para>
        /// Declaring <c>[MaxLengthField(n)]</c> on the property is always preferable: it is portable, it is
        /// visible at the model, and it applies on every provider rather than only here.
        /// </para>
        /// </remarks>
        protected virtual int IndexedStringColumnLength => 255;

        public override bool IsTransientException(Exception ex)
        {
            if (base.IsTransientException(ex)) return true;
            if (ex is MySqlException mysqlEx)
            {
                switch ((int)mysqlEx.ErrorCode)
                {
                    case 1040:  // Too many connections
                    case 1205:  // Lock wait timeout exceeded
                    case 1213:  // Deadlock found
                    case 1317:  // Query execution was interrupted
                    case 2002:  // Can't connect to local MySQL server
                    case 2003:  // Can't connect to MySQL server
                    case 2006:  // MySQL server has gone away
                    case 2013:  // Lost connection during query
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// MySQL phrases a missing table as "Table 'x' doesn't exist" (error 1146). Adds that to the base
        /// SQLite match so the reader yields an empty result rather than faulting.
        /// </summary>
        /// <remarks>
        /// TASK-211 dropped the bare <c>Message.Contains("doesn't exist")</c> catch-all that sat behind the
        /// error-code test. MySQL uses that phrasing for more than a missing table — <c>1054 Unknown column</c>
        /// aside, a missing routine reads <c>FUNCTION x doesn't exist</c> — and a reader that answers "no rows"
        /// to any of them turns an error into a plausible wrong answer. <c>1146 ER_NO_SUCH_TABLE</c> is the
        /// signal; the chain is walked because the driver wraps in some paths.
        /// </remarks>
        public override bool IsMissingTableException(Exception ex)
        {
            if (base.IsMissingTableException(ex)) return true;

            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlException mysqlEx) return (int)mysqlEx.ErrorCode == 1146;
            }

            // "Table 'db.widgets' doesn't exist" — the missing-TABLE wording, kept as the untyped fallback.
            // A missing routine reads "FUNCTION db.f does not exist", so requiring "table" is what separates
            // the error this may swallow from the ones it may not. (CR-L183 removed this pair from the
            // OnException handler as dead code — it was, THERE, because of `&&`/`||` precedence. Here it is
            // the whole test.)
            return ex.Message.Contains("table", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase);
        }

        private void MySQLConnector_OnException(Exception ex, string? commandText)
        {
            // The second operand — `Message.Contains("Table") && Message.Contains("doesn't exist")` — was
            // dead: && binds tighter than ||, so it can only be true when the first operand is already
            // true. Reduced to the single meaningful check (CR-L183).
            //
            // TASK-211: and that single check was still a message substring, so any "doesn't exist" error ran
            // DoInit() and RETURNED — reporting success for a statement that never ran. Now the same typed
            // test the reader uses, so the two cannot disagree about what "the table is missing" means.
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
        public override string QuoteIdentifier(string identifier)
        {
            return "`" + identifier.Replace("`", "``") + "`";
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Location) || string.IsNullOrEmpty(settings.Name))
            {
                throw new Exception("Invalid settings provided for MySQL connection");
            }

            if (settings is MySqlSettings mySettings)
            {
                return new MySqlConnection(mySettings.GetConnectionString());
            }

            if (settings is RemoteSettings remoteSettings)
            {
                var port = remoteSettings.Port > 0 ? remoteSettings.Port : 3306;
                var connectionString = string.Format("Server={0};Port={1};User ID={2};Password={3};Database={4}",
                    remoteSettings.Location,
                    port,
                    remoteSettings.UserName,
                    remoteSettings.Password,
                    remoteSettings.Name);
                if (remoteSettings.UseSecure)
                {
                    connectionString += ";SslMode=Required";
                }
                return new MySqlConnection(connectionString);
            }

            throw new Exception("Invalid settings provided for MySQL connection");
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
                        return string.Format("DECIMAL({0},{1})", decimalField.Precision, decimalField.Scale);
                    }
                    else
                    {
                        return "DECIMAL";
                    }
                case DbType.Double:
                    return "DOUBLE";
                case DbType.Currency:
                    return "DECIMAL(19,4)";
                case DbType.Boolean:
                    return "TINYINT(1)";
                case DbType.Time:
                    return "TIME";
                case DbType.Date:
                    return "DATE";
                case DbType.DateTime:
                case DbType.DateTime2:
                    return "DATETIME";
                case DbType.DateTimeOffset:
                    return "DATETIME";
                case DbType.Int16:
                    return "SMALLINT";
                case DbType.UInt16:
                    return "SMALLINT UNSIGNED";
                case DbType.UInt32:
                    return "INT UNSIGNED";
                case DbType.Int32:
                    return "INT";
                case DbType.Int64:
                    return "BIGINT";
                case DbType.UInt64:
                    return "BIGINT UNSIGNED";
                case DbType.Single:
                    return "FLOAT";
                case DbType.SByte:
                    return "TINYINT";
                case DbType.Byte:
                    return "TINYINT UNSIGNED";
                case DbType.Object:
                case DbType.Binary:
                    return "LONGBLOB";
                case DbType.Guid:
                    return "CHAR(36)";
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                default:
                    if (field is CharField charField)
                    {
                        return string.Format("VARCHAR({0})", charField.Lenght);
                    }
                    // TASK-248: an INDEXED unbounded string must be bounded on MySQL, because MySQL cannot
                    // index a BLOB/TEXT column without a key length -- measured on 8.4 as
                    // "ERROR 1170: BLOB/TEXT column 'x' used in key specification without a key length",
                    // for UNIQUE and plain indexes alike. So after TASK-245 fixed the statement syntax, an
                    // index over a plain `string` still could not be built here; it merely failed with 1170
                    // instead of 1064, recorded and invisible.
                    //
                    // Scoped to MySQL on purpose -- but note the reason narrowed in TASK-257. SQLite (type
                    // affinity) and PostgreSQL (btree over TEXT) index an unbounded string happily and
                    // genuinely ignore this flag. **MSSql does not**, and the comment that once stood here
                    // said it did: it emitted TEXT, which SQL Server refuses as an index key (Msg 1919), so
                    // no declared index over an unlengthed string had ever been built there either. MSSql now
                    // bounds such a column too, reading the wider AbstractField.IsInIndexKey.
                    //
                    // This branch deliberately still reads the narrower IsIndexed, so MySQL has the same hole
                    // MSSql just closed: [UniqueField]/[PrimaryField] on an unlengthed string emits
                    // `LONGTEXT UNIQUE`, which is ERROR 1170 at CREATE TABLE. Switching to IsInIndexKey is a
                    // one-word change, but it alters DDL on this provider and needs a live 8.4 measurement
                    // first -- it is filed rather than guessed. Do not "unify" it from symmetry.
                    //
                    // 7 live consumer entities (Symbio's docnumber/email UNIQUE composites) declare exactly
                    // this shape and work correctly on PostgreSQL today -- so refusing the declaration
                    // framework-wide, or bounding the column on every provider, would break working
                    // deployments to fix one provider. MySQL's own 3072-byte index-key limit means SOME bound
                    // is unavoidable here regardless: the divergence is the provider's, not the framework's.
                    //
                    // A prefix index (`ux(Col(64))`) was rejected: every real case is UNIQUE, and a prefix
                    // makes the constraint WEAKER than declared -- it rejects two genuinely different values
                    // whose first n characters match. A bounded column refuses the over-long write instead,
                    // which is a loud, correct failure rather than a quiet, wrong constraint.
                    else if (field.IsIndexed)
                    {
                        return string.Format("VARCHAR({0})", IndexedStringColumnLength);
                    }
                    else
                    {
                        return "LONGTEXT";
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
                result.AppendFormat(" {0}", ConvertType(field.Type, field));
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
                if (field.IsAutoincrement)
                {
                    result.AppendFormat(" AUTO_INCREMENT");
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
                ((MySqlParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
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

        #region Index DDL — MySQL has no conditional form (TASK-245)

        /// <summary>
        /// MySQL rejects <c>IF NOT EXISTS</c> on <c>CREATE INDEX</c>, so this never emits it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-245. Measured on MySQL 8.4: <c>CREATE INDEX IF NOT EXISTS ix ON T (v)</c> is
        /// <c>ERROR 1064</c>, a syntax error — so the base statement never ran and <b>every</b> declared
        /// index on a MySQL entity was absent, silently, since TASK-204 made schema-ensure record rather
        /// than throw. MSSql overrides the emitter with a <c>sys.indexes</c> guard and PostgreSQL/SQLite
        /// support the clause natively, which left MySQL as the one provider that neither overrode nor
        /// supported it.
        /// </para>
        /// <para>
        /// The <c>conditional</c> parameter is therefore accepted and <b>ignored for the statement</b>:
        /// MySQL has no conditional spelling either way. What it controls is whether
        /// <c>CreateIndexes</c> tolerates the resulting error — see
        /// <see cref="IsIndexAlreadyExistsException"/>. Columns are emitted bare and the table quoted,
        /// inherited from the base rather than re-decided here (§ Conventions).
        /// </para>
        /// </remarks>
        public override string CreateIndexSql(string tableName, Tables.IndexDefinition index, bool conditional = true)
        {
            var columns = string.Join(", ", index.Columns.Select(c =>
                c.ColumnName + (c.IsDescending ? " DESC" : "")));

            var unique = index.Unique ? "UNIQUE " : "";
            return $"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(tableName)} ({columns})";
        }

        /// <summary>
        /// MySQL's <c>DROP INDEX</c> takes no <c>IF EXISTS</c> and <b>requires</b> <c>ON &lt;table&gt;</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-245 — the base emitted <c>DROP INDEX IF EXISTS `name`</c>, which is wrong twice over on
        /// MySQL: measured <c>ERROR 1064</c> for the <c>IF EXISTS</c>, and the mandatory <c>ON</c> clause
        /// missing entirely. So no declared index could be dropped here either.
        /// </para>
        /// <para>
        /// <b>Dropping an absent index therefore throws</b> (<c>ERROR 1091</c>), where the base's
        /// <c>IF EXISTS</c> tolerated it. That is deliberate and provider-local: a <c>DropIndexes</c> caller
        /// named a specific index, and the migrations <c>SqlSchemaBuilder.DropIndex</c> step should fail
        /// loudly rather than silently skip. It is <b>not</b> paired with an "already gone" tolerance,
        /// because unlike <c>CREATE</c> there is no conditional form being emulated here.
        /// </para>
        /// </remarks>
        public override string DropIndexSql(string tableName, Tables.IndexDefinition index)
        {
            return $"DROP INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(tableName)}";
        }

        /// <summary>
        /// MySQL error <b>1061</b> — <c>Duplicate key name</c> — is "the index you asked for is already
        /// there", which is what the other three providers report as success.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-245. Matched on the error <b>code</b>, never the message, and the <c>InnerException</c> chain
        /// is walked because <c>AbstractConnector.InitException</c> re-throws every command failure as
        /// <c>new Exception(commandText, ex)</c> — the same reason
        /// <see cref="IsMissingTableException"/> walks it.
        /// </para>
        /// <para>
        /// <b>1061 only.</b> 1062 (<c>Duplicate entry</c>) is a UNIQUE index over data that already violates
        /// it — genuinely unbuildable, and it must keep reaching the recorder so TASK-204 holds; 1170
        /// (<c>BLOB/TEXT column used in key specification without a key length</c>) is an unbounded string
        /// column, also unbuildable. Widening this predicate to any <c>MySqlException</c> would swallow both.
        /// </para>
        /// <para>
        /// Note 1061 also fires for a same-name index over <i>different</i> columns, so such an index is
        /// silently accepted. That is faithful rather than a hole: measured on PostgreSQL 16,
        /// <c>CREATE INDEX IF NOT EXISTS</c> likewise reports <i>"relation already exists, skipping"</i> and
        /// keeps the old definition, and MSSql's guard compares the name alone.
        /// </para>
        /// </remarks>
        public override bool IsIndexAlreadyExistsException(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlException mysqlEx)
                {
                    return (int)mysqlEx.ErrorCode == 1061;  // ER_DUP_KEYNAME
                }
            }
            return false;
        }

        /// <summary>
        /// MySQL error <b>1091</b> — <c>Can't DROP '...'; check that column/key exists</c> — is "the index you
        /// asked to drop is already gone", which every other provider reports as success because its
        /// <c>DROP INDEX</c> carries <c>IF EXISTS</c> and MySQL's cannot.
        /// </summary>
        /// <remarks>
        /// TASK-249, and it is consumed only by <c>SqlIndexManager.DropAsync</c> — not by the connector's
        /// <c>DropIndexes</c>, which must keep failing loudly for a caller that named a specific index.
        /// Matched on the code, chain-walked, for the same reasons as the 1061 twin above.
        /// </remarks>
        public override bool IsIndexMissingException(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlException mysqlEx)
                {
                    return (int)mysqlEx.ErrorCode == 1091;  // ER_CANT_DROP_FIELD_OR_KEY
                }
            }
            return false;
        }

        #endregion

        #region Native Bulk Operations

        // NOTE (CR-L185): if a bulk operation fails because the table does not yet exist, the transaction
        // is rolled back and InitException -> MySQLConnector_OnException creates the table (DoInit) but does
        // NOT re-run the bulk command, so the bulk payload is silently dropped (no exception, no retry).
        // This is the inherited framework auto-init behavior shared by the other SQL dialects — the
        // single-row CRUD path tolerates it, the bulk path does not. Callers must ensure the schema exists
        // (InitAsync / a prior single-row write / CreateTable) before the first bulk operation.

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

            var fieldCount = fields.Count;
            var batchSize = _settings is MySqlSettings mySettings ? mySettings.BulkInsertBatchSize : DefaultBulkInsertBatchSize;
            var maxBatchSize = Math.Min(batchSize, 65535 / fieldCount);
            if (maxBatchSize < 1)
                maxBatchSize = 1;

            var columnNames = string.Join(", ", fields.Select(f => QuoteIdentifier(f.Name)));
            var modelList = models as IList<object> ?? models.ToList();

            // A bulk write must JOIN an open boundary on this database rather than open a second connection.
            // On MySQL two connections are perfectly legal, so before this the statements committed on their
            // own transaction and SURVIVED the owner's rollback with no error anywhere — the quiet half of
            // the defect, and the one most likely to be in production. retryWhenOwned: false keeps the
            // own-connection path exactly as it shipped; it never retried and this fix is not the place to
            // start (see AbstractConnector.RunBulk).
            RunBulk("BulkInsert into " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    for (var batchStart = 0; batchStart < modelList.Count; batchStart += maxBatchSize)
                    {
                        var batchEnd = Math.Min(batchStart + maxBatchSize, modelList.Count);
                        var batchCount = batchEnd - batchStart;

                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;

                        var sb = new StringBuilder();
                        sb.Append("INSERT INTO ");
                        sb.Append(QuoteIdentifier(table.Name));
                        sb.Append(" (");
                        sb.Append(columnNames);
                        sb.Append(") VALUES ");

                        for (var rowIdx = 0; rowIdx < batchCount; rowIdx++)
                        {
                            if (rowIdx > 0)
                                sb.Append(", ");

                            sb.Append('(');
                            for (var fieldIdx = 0; fieldIdx < fieldCount; fieldIdx++)
                            {
                                if (fieldIdx > 0)
                                    sb.Append(", ");

                                var paramName = "@P" + rowIdx + "_" + fieldIdx;
                                sb.Append(paramName);

                                var model = modelList[batchStart + rowIdx];
                                command.Parameters.Add(new MySqlParameter(paramName, fields[fieldIdx].Write(model) ?? DBNull.Value));
                            }
                            sb.Append(')');
                        }

                        command.CommandText = sb.ToString();
                        commandText = command.CommandText;
                        command.ExecuteNonQuery();
                    }

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkInsert into " + table.Name);
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

            var fieldCount = fields.Count;
            var batchSize = _settings is MySqlSettings mySettings ? mySettings.BulkInsertBatchSize : DefaultBulkInsertBatchSize;
            var maxBatchSize = Math.Min(batchSize, 65535 / fieldCount);
            if (maxBatchSize < 1)
                maxBatchSize = 1;

            var columnNames = string.Join(", ", fields.Select(f => QuoteIdentifier(f.Name)));
            var modelList = models as IList<object> ?? models.ToList();

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            await RunBulkAsync("BulkInsertAsync into " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    for (var batchStart = 0; batchStart < modelList.Count; batchStart += maxBatchSize)
                    {
                        ct.ThrowIfCancellationRequested();

                        var batchEnd = Math.Min(batchStart + maxBatchSize, modelList.Count);
                        var batchCount = batchEnd - batchStart;

                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;

                        var sb = new StringBuilder();
                        sb.Append("INSERT INTO ");
                        sb.Append(QuoteIdentifier(table.Name));
                        sb.Append(" (");
                        sb.Append(columnNames);
                        sb.Append(") VALUES ");

                        for (var rowIdx = 0; rowIdx < batchCount; rowIdx++)
                        {
                            if (rowIdx > 0)
                                sb.Append(", ");

                            sb.Append('(');
                            for (var fieldIdx = 0; fieldIdx < fieldCount; fieldIdx++)
                            {
                                if (fieldIdx > 0)
                                    sb.Append(", ");

                                var paramName = "@P" + rowIdx + "_" + fieldIdx;
                                sb.Append(paramName);

                                var model = modelList[batchStart + rowIdx];
                                command.Parameters.Add(new MySqlParameter(paramName, fields[fieldIdx].Write(model) ?? DBNull.Value));
                            }
                            sb.Append(')');
                        }

                        command.CommandText = sb.ToString();
                        commandText = command.CommandText;
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
                    InitException(ex, commandText ?? "BulkInsertAsync into " + table.Name);
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            RunBulk("BulkUpdate " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            await RunBulkAsync("BulkUpdateAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            RunBulk("BulkDelete " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            await RunBulkAsync("BulkDeleteAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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
