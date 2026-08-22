using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.Stores;
using MSSqlSettings = Birko.Data.SQL.MSSql.Stores.MSSqlSettings;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    public partial class MSSqlConnector : AbstractAsyncConnector
    {
        public MSSqlConnector(Birko.Configuration.RemoteSettings settings) : base(settings)
        {
            OnException += MSSqlConnector_OnException;
        }

        private void MSSqlConnector_OnException(Exception ex, string? commandText)
        {
            if (!IsInitializing && ex.Message.Contains("Invalid object name"))
            {
                DoInit();
            }
            else
            {
                throw new Exception(commandText, ex);
            }
        }


        /// <summary>
        /// Detects SQL Server transient errors: deadlocks (1205), timeouts (-2),
        /// transport-level errors (20), login failures (64), resource busy (49920), etc.
        /// </summary>
        public override bool IsTransientException(Exception ex)
        {
            if (base.IsTransientException(ex)) return true;
            if (ex is SqlException sqlEx)
            {
                foreach (SqlError error in sqlEx.Errors)
                {
                    switch (error.Number)
                    {
                        case -2:     // Timeout
                        case 20:     // Transport-level error
                        case 64:     // Connection was established but login failed
                        case 233:    // Connection closed by server
                        case 1205:   // Deadlock victim
                        case 10053:  // Transport-level error (network)
                        case 10054:  // Connection reset by peer
                        case 10060:  // Network timeout
                        case 40143:  // Connection could not be initialized
                        case 40197:  // Service error processing request
                        case 40501:  // Service busy
                        case 40613:  // Database unavailable
                        case 49918:  // Not enough resources
                        case 49919:  // Cannot process request (too many operations)
                        case 49920:  // Cannot process request (too many operations)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// SQL Server phrases a missing table as "Invalid object name 'x'" (error 208). Adds that to the
        /// base SQLite match so the reader yields an empty result rather than faulting.
        /// </summary>
        public override bool IsMissingTableException(Exception ex)
        {
            if (base.IsMissingTableException(ex)) return true;
            if (ex is SqlException sqlEx)
            {
                foreach (SqlError error in sqlEx.Errors)
                {
                    if (error.Number == 208) return true;
                }
            }
            return ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase);
        }

        public override string QuoteIdentifier(string identifier)
        {
            return "[" + identifier.Replace("]", "]]") + "]";
        }

        /// <inheritdoc />
        /// <remarks>Bracket-quoted, so the delimiters differ from each other (TASK-262).</remarks>
        protected override char IdentifierQuoteOpen => '[';

        /// <inheritdoc />
        protected override char IdentifierQuoteClose => ']';

        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Location) || string.IsNullOrEmpty(settings.Name))
            {
                throw new Exception("No path provided");
            }

            if (settings is MSSqlSettings msSettings)
            {
                return new SqlConnection(msSettings.GetConnectionString());
            }

            if (settings is RemoteSettings remotesettings)
            {
                // CR-M136: build the connection string through MSSqlSettings.GetConnectionString()
                // rather than a divergent inline string. The previous inline form hard-coded
                // MultipleActiveResultSets=False and omitted TrustServerCertificate / Connection Timeout,
                // so behaviour silently differed from the typed MSSqlSettings path. Deriving an
                // MSSqlSettings from the RemoteSettings values yields one consistent builder.
                var derived = new MSSqlSettings(
                    remotesettings.Location!,
                    remotesettings.Name!,
                    remotesettings.UserName,
                    remotesettings.Password,
                    remotesettings.Port,
                    remotesettings.UseSecure);
                return new SqlConnection(derived.GetConnectionString());
            }

            throw new Exception("No path provided");
        }

        /// <summary>
        /// Column length used for a string that declares no explicit length but sits in an <b>index key</b>
        /// (TASK-257).
        /// </summary>
        /// <remarks>
        /// 255 characters = 510 bytes under NVARCHAR. That fits SQL Server's index-key limits (1700 bytes
        /// nonclustered, 900 clustered) for a single-column key and for a composite of up to <b>three</b> such
        /// columns (1530 bytes) — measured on 2022.
        /// <para>
        /// <b>It does NOT cap the per-index total, and four of them overflow.</b> A <c>[CompositeIndex]</c> over
        /// four unlengthed strings is 4 × 510 = 2040 bytes: SQL Server <i>creates</i> that index (with warning
        /// 1708) and then fails the <b>INSERT</b> with Msg 1946, <i>"The index entry of length 2040 bytes …
        /// exceeds the maximum length of 1700 bytes"</i> — measured. So a wide composite trades this task's
        /// loud DDL failure for a deferred, data-dependent write failure. Nothing here prevents that; declare
        /// <c>[MaxLengthField(n)]</c> on the participating columns when a composite has four or more string
        /// members. Recorded rather than fixed because capping a per-index budget is a different design
        /// question from typing a column, and it needs its own task.
        /// </para>
        /// <para>
        /// The number matches <c>MySQLConnector.IndexedStringColumnLength</c> on purpose: a value that indexes
        /// on one server must index on the other, because the same model runs on both. Per-provider headroom
        /// would buy nothing real — the columns that actually get indexed are document numbers, codes and
        /// e-mail addresses — and would cost a divergence someone later has to explain.
        /// </para>
        /// <para>
        /// <b>Raising it in a derived connector is not a one-line escape hatch.</b> <c>MSSqlStore&lt;T&gt;</c> /
        /// <c>AsyncMSSqlStore&lt;T&gt;</c> bind <c>MSSqlConnector</c> as their concrete type argument and
        /// <c>DataBase.GetConnector&lt;DB&gt;</c> instantiates exactly that type, so a subclass overriding this
        /// property never reaches DDL through any shipped store — a consumer must also declare its own store
        /// bound to the derived connector. Stated because a guard whose opt-out does not actually open is a
        /// wall wearing a door's label (§ SH-H037).
        /// </para>
        /// <para>
        /// Declaring <c>[MaxLengthField(n)]</c> on the property is preferable and needs none of that: it is
        /// visible at the model and honoured on MSSql, MySQL and PostgreSQL. It is <i>not</i> honoured on
        /// SQLite, which returns <c>TEXT</c> for every string and never reads the declared length — harmless
        /// there, since type affinity indexes it anyway, but "portable to every provider" would overstate it.
        /// </para>
        /// </remarks>
        protected virtual int IndexedStringColumnLength => 255;

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
                    return "FLOAT";
                case DbType.Currency:
                    return "Money";
                case DbType.Boolean:
                    return "BIT";
                case DbType.Time:
                    return "TIME";
                case DbType.Date:
                    return "DATE";
                case DbType.DateTime:
                case DbType.DateTime2:
                    // DbType.DateTime previously mapped to DATE, silently truncating time-of-day on
                    // every DateTime column. Map to DATETIME2 (SQL Server's full-precision type) so
                    // timestamped/audit fields keep their time component (CR-H086).
                    return "DATETIME2";
                case DbType.DateTimeOffset:
                    return "DateTimeOffset";
                case DbType.Int16:
                case DbType.UInt16:
                    return "SMALLINT";
                case DbType.UInt32:
                case DbType.Int32:
                    return "INT";
                case DbType.Int64:
                case DbType.UInt64:
                    return "BIGINT";
                case DbType.Single:
                    // A C# float grouped with SByte/Byte generated a TINYINT (0-255) column,
                    // truncating the value and dropping negatives/fractions. REAL is SQL Server's
                    // 4-byte single-precision float (CR-H087).
                    return "REAL";
                case DbType.SByte:
                case DbType.Byte:
                    return "TINYINT";
                case DbType.Xml:
                    return "XML";
                case DbType.Object:
                case DbType.Binary:
                    // BINARY with no length defaults to BINARY(1) in SQL Server, truncating any
                    // blob/serialized object to a single byte. Use VARBINARY(MAX) (CR-M137).
                    return "VARBINARY(MAX)";
                case DbType.Guid:
                    return "UNIQUEIDENTIFIER";
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                default:
                    if (field is CharField charField)
                    {
                        return string.Format("NVARCHAR({0})", charField.Lenght);
                    }
                    // TASK-257: an unlengthed string used to become TEXT here, and TEXT is unusable in a
                    // predicate on SQL Server -- parameters bind as nvarchar, so every comparison is a type
                    // clash. Measured on 2022 (16.0.4265.3) against a TEXT column:
                    //
                    //   `= @p` / `<> @p` / `IN (@p)`  -> Msg 402  "The data types text and nvarchar are
                    //                                             incompatible in the equal to operator"
                    //   LOWER(col)                    -> Msg 8116
                    //   ORDER BY col / GROUP BY col   -> Msg 306
                    //   SELECT DISTINCT col           -> Msg 421
                    //   CREATE INDEX / inline UNIQUE  -> Msg 1919
                    //   LIKE @p / IS NULL             -> legal, the only two that worked
                    //
                    // So a plain `public string Name { get; set; }` -- the common consumer shape, present on
                    // essentially every consumer entity -- made every Find/Count/DeleteWhere predicate and
                    // every SortBy over that column throw. NVARCHAR(MAX) fixes all of it (measured: all six
                    // refused operations above succeed) and is not deprecated, which TEXT is.
                    //
                    // MAX rather than a bounded default: TEXT accepts 2GB writes today and only READS failed,
                    // so any bounded default would start refusing values the same code stored yesterday. It
                    // also keeps MSSql in step with SQLite TEXT / PostgreSQL TEXT / MySQL LONGTEXT, all
                    // unbounded -- the CR-M137 VARBINARY(MAX) arm above was chosen for the same reason.
                    // `field == null` first so this stays null-tolerant: every other arm here uses an `is`
                    // pattern that is simply false for null, and the pre-TASK-257 code returned a type for a
                    // null field rather than throwing. SqLite and PostgreSQL are still null-tolerant, so a
                    // bare `!field.IsInIndexKey` would make this the only provider that NREs on public
                    // surface the tests exercise deliberately.
                    else if (field == null || !field.IsInIndexKey)
                    {
                        return "NVARCHAR(MAX)";
                    }
                    // An index KEY cannot be a MAX type on SQL Server at all -- measured, an index over
                    // NVARCHAR(MAX) raises the *same* Msg 1919 as TEXT does, so the MAX change alone would
                    // have left every declared index over an unlengthed string exactly as broken. Worse for
                    // UNIQUE/PRIMARY KEY, which FieldDefinition emits as inline column constraints: those
                    // took down the whole CREATE TABLE, not just an index.
                    //
                    // IsInIndexKey, not IsIndexed, precisely because of that: LoadIndexes marks only
                    // [IndexedField]/[CompositeIndex] columns, never a [UniqueField] or [PrimaryField] one.
                    //
                    // 255 matches MySQL's IndexedStringColumnLength deliberately -- a value that indexes on
                    // one server must index on the other, since the same model runs on both. The real ceiling
                    // is SQL Server's key limit (1700 bytes nonclustered / 900 clustered = 850 / 450 chars at
                    // 2 bytes each), not this number; raise it in a derived connector if a consumer genuinely
                    // indexes longer values. Prefix indexes are not available here, so a bounded column is
                    // the only option -- and it refuses an over-long write loudly rather than silently
                    // weakening a UNIQUE constraint, which is the TASK-248 trade.
                    else
                    {
                        return string.Format("NVARCHAR({0})", IndexedStringColumnLength);
                    }
            }
        }

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
                    result.AppendFormat(" IDENTITY(1,1)");
                }
            }
            return result.ToString();
        }

        public override string LimitOffsetDefinition(DbCommand command, int? limit = null, int? offset = null)
        {
            var result = new StringBuilder();
            if (limit != null)
            {
                if (offset != null)
                {
                    result.Append(" OFFSET @OFFSET ROWS");
                    AddParameter(command, "@OFFSET", offset.Value);
                }
                result.Append(" FETCH NEXT @LIMIT ROWS ONLY");
                AddParameter(command, "@LIMIT", limit.Value);
            }
            return result.ToString();
        }

        public override DbCommand AddParameter(DbCommand command, string name, object? value)
        {
            // Enums persist as INTEGER (IntegerField) — bind the underlying integral value, never the
            // boxed enum, or the provider maps it to its own type and the comparison never matches.
            value = NormalizeParameterValue(value);
            if (command.Parameters.Contains(name))
            {
                ((SqlParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
            }
            else
            {
                ((SqlCommand)command).Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
            return command;
        }

        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            // DoDdlCommand, not DoCommand: on a provider whose DDL is not transactional this must not run
            // on an ambient boundary's connection, because the statement would implicitly commit it
            // (TASK-243). inOwnTransaction: false keeps this emitter autocommitted exactly as it was.
            DoDdlCommand((command) =>
            {
                command.CommandText =
                    "IF NOT EXISTS (SELECT * FROM sys.tables WHERE name='" + SqlLiteral.EscapeLiteral(name) +"') "
                    + "CREATE TABLE "
                    + QuoteIdentifier(name)
                    + " ("
                    + string.Join(", ", fields.Where(x => !string.IsNullOrEmpty(x)))
                    + ")";
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true, inOwnTransaction: false);
        }

        /// <summary>
        /// MSSql has no <c>IF NOT EXISTS</c> on <c>CREATE INDEX</c>, so the conditional form is synthesised
        /// with a <c>sys.indexes</c> guard.
        /// </summary>
        /// <remarks>
        /// TASK-245 — when <paramref name="conditional"/> is false the guard is omitted, so an
        /// already-present index raises rather than being skipped. That is what makes
        /// <c>CreateIndexes(..., throwIfExists: true)</c> mean the same thing here as on every other
        /// provider instead of being silently ignored.
        /// <para>
        /// Column identifiers stay bracket-quoted here, deliberately unlike the base (which emits them bare
        /// for PostgreSQL's sake). MSSql resolves either spelling — its identifiers are case-insensitive
        /// under the default collation — so there is no defect to fix and no live MSSql measurement backing
        /// a change. Note the guard matches on index <b>name</b> only, so a same-name index over different
        /// columns is skipped; that matches MySQL 1061 and PostgreSQL's own IF NOT EXISTS.
        /// </para>
        /// </remarks>
        public override string CreateIndexSql(string tableName, Tables.IndexDefinition index, bool conditional = true)
        {
            var columns = string.Join(", ", index.Columns.Select(c =>
                QuoteIdentifier(c.ColumnName) + (c.IsDescending ? " DESC" : "")));

            var indexName = SqlLiteral.EscapeLiteral(index.Name);
            var unique = index.Unique ? "UNIQUE " : "";
            // TASK-273 — the filtered tail belongs to the CREATE, so it is appended before the guard is
            // prefixed: both the conditional and the plain form must carry it. Measured on SQL Server 2022:
            // the guarded filtered create runs, and sys.indexes then reports
            // has_filter=True filter=([DeletedAt] IS NULL).
            var create = $"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(tableName)} ({columns})"
                       + IndexPredicateClause(index);
            if (!conditional)
            {
                return create;
            }
            return $"IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='{indexName}' AND object_id=OBJECT_ID('{SqlLiteral.EscapeLiteral(tableName)}')) "
                 + create;
        }

        /// <summary>
        /// Predicate columns are bracket-quoted here, matching this override's key-column list rather than
        /// the base's bare spelling (TASK-273).
        /// </summary>
        /// <remarks>
        /// The base emits columns bare because PostgreSQL case-folds an unquoted identifier and
        /// <c>CreateTable</c> creates them that way; MSSql resolves either spelling under its
        /// case-insensitive default collation, and this class already quotes its key columns deliberately.
        /// Splitting the two here — quoted keys, bare predicate — would be the one place in the framework
        /// where an index's own column list disagrees with itself.
        /// </remarks>
        protected override string PredicateColumn(string columnName) => QuoteIdentifier(columnName);

        public override string DropIndexSql(string tableName, Tables.IndexDefinition index)
        {
            var indexName = SqlLiteral.EscapeLiteral(index.Name);
            return $"IF EXISTS (SELECT * FROM sys.indexes WHERE name='{indexName}' AND object_id=OBJECT_ID('{SqlLiteral.EscapeLiteral(tableName)}')) "
                 + $"DROP INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(tableName)}";
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
            var dataTable = BuildDataTable(table.Name, fields, models);

            // A bulk write must JOIN an open boundary on this database rather than open a second connection.
            // On MSSql two connections are perfectly legal, so before this the copy committed independently
            // and SURVIVED the owner's rollback with no error anywhere — the quiet half of the defect.
            //
            // SqlBulkCopy enlists in an external transaction ONLY through the
            // SqlBulkCopy(SqlConnection, SqlBulkCopyOptions, SqlTransaction) overload. That third argument
            // was null, which is exactly why the copy escaped; handing it the boundary's transaction is the
            // whole fix, and the boundary's commit is then what makes the rows durable.
            //
            // TableLock is KEPT when this owns the connection and DROPPED when participating. A bulk-update
            // (BU) table lock taken by a standalone copy is released as soon as that copy ends; taken inside
            // somebody else's boundary it is held until THEIR commit, serialising every other writer against
            // the table for the whole life of a transaction that never asked for it. The standalone fast
            // path is unchanged.
            //
            // RunBulkOnConnection rather than RunBulk: SqlBulkCopy carries its own atomicity and ran with no
            // enclosing transaction here, so the owned path is left exactly as it was.
            RunBulkOnConnection("BulkInsert into " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqlConnection)dbConnection;
                try
                {
                    var options = owned ? SqlBulkCopyOptions.TableLock : SqlBulkCopyOptions.Default;
                    using var bulkCopy = new SqlBulkCopy(connection, options, (SqlTransaction?)dbTransaction);
                    bulkCopy.DestinationTableName = QuoteIdentifier(table.Name);
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                    }
                    bulkCopy.WriteToServer(dataTable);
                }
                catch (Exception ex)
                {
                    InitException(ex, "BulkInsert into " + table.Name);
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
            var dataTable = BuildDataTable(table.Name, fields, models);

            // Joins an open boundary instead of opening a second connection, and enlists the copy in it via
            // the SqlTransaction overload — see BulkInsert above for why TableLock is owned-path only.
            await RunBulkOnConnectionAsync("BulkInsertAsync into " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqlConnection)dbConnection;
                try
                {
                    var options = owned ? SqlBulkCopyOptions.TableLock : SqlBulkCopyOptions.Default;
                    using var bulkCopy = new SqlBulkCopy(connection, options, (SqlTransaction?)dbTransaction);
                    bulkCopy.DestinationTableName = QuoteIdentifier(table.Name);
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                    }
                    await bulkCopy.WriteToServerAsync(dataTable, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InitException(ex, "BulkInsertAsync into " + table.Name);
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
            // On MSSql two connections are perfectly legal, so before this the statements committed on their
            // own transaction and SURVIVED the owner's rollback with no error anywhere. retryWhenOwned: false
            // keeps the own-connection path exactly as it shipped; it never retried.
            //
            // CR-L179 kept Open()/BeginTransaction() inside the try so an open failure routed through
            // InitException. Acquiring the connection is now the shared helper's job — which is what gives
            // "am I inside a boundary" a single producer — so an open failure propagates raw instead of
            // wrapped in Exception(commandText, ex). Nothing is swallowed either way: MSSqlConnector_OnException
            // rethrows for anything but "Invalid object name", which an open failure is not. It also puts the
            // bulk path in step with the framework's single-command path, whose RunCommandTransaction has
            // always opened outside its try.
            RunBulk("BulkUpdate " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqlConnection)dbConnection;
                var transaction = (SqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new SqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    // NOT command.Prepare(): SqlCommand requires every parameter to have an explicitly set
                    // type before it will prepare, and these are created as `new SqlParameter(name,
                    // DBNull.Value)` placeholders whose type is only implied by the value assigned per
                    // row. It therefore threw "SqlCommand.Prepare method requires all parameters to have
                    // an explicitly set type" on the very first row — measured against SQL Server 2022,
                    // which means BulkUpdate and BulkDelete have never worked on this provider at all,
                    // in either half. Found by this task's regression suite, which could not otherwise
                    // reach the boundary behaviour it exists to prove. Dropping the call is the whole
                    // repair: SQL Server caches the plan for a repeated parameterised statement on its
                    // own, so Prepare bought nothing here even where it did not throw. (Npgsql and
                    // MySqlConnector infer the missing types, which is why only this provider broke.)

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

            // Joins an open boundary instead of opening a second connection — see BulkUpdate above, including
            // why CR-L179's in-try acquisition does not survive the move to the shared helper.
            await RunBulkAsync("BulkUpdateAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqlConnection)dbConnection;
                var transaction = (SqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new SqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    // NOT command.Prepare(): SqlCommand requires every parameter to have an explicitly set
                    // type before it will prepare, and these are created as `new SqlParameter(name,
                    // DBNull.Value)` placeholders whose type is only implied by the value assigned per
                    // row. It therefore threw "SqlCommand.Prepare method requires all parameters to have
                    // an explicitly set type" on the very first row — measured against SQL Server 2022,
                    // which means BulkUpdate and BulkDelete have never worked on this provider at all,
                    // in either half. Found by this task's regression suite, which could not otherwise
                    // reach the boundary behaviour it exists to prove. Dropping the call is the whole
                    // repair: SQL Server caches the plan for a repeated parameterised statement on its
                    // own, so Prepare bought nothing here even where it did not throw. (Npgsql and
                    // MySqlConnector infer the missing types, which is why only this provider broke.)

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

            // Joins an open boundary instead of opening a second connection — see BulkUpdate above, including
            // why CR-L179's in-try acquisition does not survive the move to the shared helper.
            RunBulk("BulkDelete " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqlConnection)dbConnection;
                var transaction = (SqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    // NOT command.Prepare(): SqlCommand requires every parameter to have an explicitly set
                    // type before it will prepare, and these are created as `new SqlParameter(name,
                    // DBNull.Value)` placeholders whose type is only implied by the value assigned per
                    // row. It therefore threw "SqlCommand.Prepare method requires all parameters to have
                    // an explicitly set type" on the very first row — measured against SQL Server 2022,
                    // which means BulkUpdate and BulkDelete have never worked on this provider at all,
                    // in either half. Found by this task's regression suite, which could not otherwise
                    // reach the boundary behaviour it exists to prove. Dropping the call is the whole
                    // repair: SQL Server caches the plan for a repeated parameterised statement on its
                    // own, so Prepare bought nothing here even where it did not throw. (Npgsql and
                    // MySqlConnector infer the missing types, which is why only this provider broke.)

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

            // Joins an open boundary instead of opening a second connection — see BulkUpdate above, including
            // why CR-L179's in-try acquisition does not survive the move to the shared helper.
            await RunBulkAsync("BulkDeleteAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (SqlConnection)dbConnection;
                var transaction = (SqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    // NOT command.Prepare(): SqlCommand requires every parameter to have an explicitly set
                    // type before it will prepare, and these are created as `new SqlParameter(name,
                    // DBNull.Value)` placeholders whose type is only implied by the value assigned per
                    // row. It therefore threw "SqlCommand.Prepare method requires all parameters to have
                    // an explicitly set type" on the very first row — measured against SQL Server 2022,
                    // which means BulkUpdate and BulkDelete have never worked on this provider at all,
                    // in either half. Found by this task's regression suite, which could not otherwise
                    // reach the boundary behaviour it exists to prove. Dropping the call is the whole
                    // repair: SQL Server caches the plan for a repeated parameterised statement on its
                    // own, so Prepare bought nothing here even where it did not throw. (Npgsql and
                    // MySqlConnector infer the missing types, which is why only this provider broke.)

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

        private DataTable BuildDataTable(string tableName, IList<AbstractField> fields, IEnumerable<object> models)
        {
            var dataTable = new DataTable(tableName);
            foreach (var field in fields)
            {
                var colType = DbTypeToClrType(field.Type);
                dataTable.Columns.Add(field.Name, colType);
            }

            foreach (var model in models)
            {
                var row = dataTable.NewRow();
                foreach (var field in fields)
                {
                    var value = field.Write(model);
                    row[field.Name] = value ?? DBNull.Value;
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        #endregion
    }
}
