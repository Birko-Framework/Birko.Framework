global using Birko.Tests.Shared;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace Birko.Tests.Shared;

/// <summary>
/// The one producer of the database a live SQL test runs in: a database of its own per test PROCESS, created on first
/// use and dropped after the assembly's last test by <see cref="LiveTestFramework"/> (TASK-883).
/// </summary>
/// <remarks>
/// <para>
/// Every live suite used to share the database named by <c>BIRKO_{PG,MYSQL,MSSQL,TS}_DB</c> (default
/// <c>birkoview</c>), and every table, view, index and type in it has a fixed name — most from a <c>[Table("…")]</c>
/// attribute, which cannot take a runtime suffix. Measured 2026-11-11: two concurrent runs of
/// <c>Birko.Data.SQL.PostgreSQL.Tests</c> failed 1,571 and 1,570 of 2,602 tests (alone: 2,602 of 2,602), each run
/// dropping and refilling the other's tables — duplicate keys, the <c>pg_type_typname_nsp_index</c> violation,
/// "Invalid object name", rows counted twice.
/// </para>
/// <para>
/// A database per run isolates every name at once — attribute-declared tables, hand-written <c>CREATE</c> statements,
/// views, indexes, the migrations bookkeeping tables — without touching any of them. A table-name override was the
/// alternative and was rejected: index and constraint names (schema-scoped on PostgreSQL) and literal SQL in the live
/// tests would each need their own suffix, and missing one is a collision nobody sees until two runs overlap.
/// </para>
/// <para>
/// <see cref="Name"/> returns the configured name unchanged while <c>BIRKO_{prefix}_HOST</c> is unset, so a suite with
/// no live server still skips (or fails under <c>BIRKO_REQUIRE_LIVE</c>) exactly as before, without a connection
/// attempt. A database something else must create beside the run database — a second MySQL database, a database with
/// another collation or time zone — takes its name from <see cref="RunName"/>, so it carries the same <see cref="Tag"/>
/// and is dropped with it. A run killed before its teardown leaves its databases behind; the next run on that server drops any
/// tagged database older than <see cref="AbandonedAfter"/>.
/// </para>
/// <para>
/// Drivers are reached by reflection, as <see cref="TestTempDirectory"/> reaches SQLite, so this one file links into
/// every live test project whichever drivers it references.
/// </para>
/// </remarks>
internal static class LiveDatabase
{
    /// <summary>This process's run tag: <c>r</c>, the UTC start time (<c>yyMMddHHmmss</c>) and 4 random hex digits —
    /// lower-case and digits only, so a name carrying it survives case folding and needs no quoting.</summary>
    public static string Tag { get; } =
        "r" + DateTime.UtcNow.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + RandomNumberGenerator.GetHexString(4, lowercase: true);

    /// <summary>A tagged database older than this belongs to a run that never reached its teardown.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(6);

    private static readonly Regex Tagged = new(@"(?:^|_)r(\d{12})[0-9a-f]{4}(?:_|$)", RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, Lazy<Server>> Servers = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<Server> Created = new();

    /// <summary>
    /// The database a live test against the server named by <paramref name="prefix"/> (<c>PG</c>, <c>MYSQL</c>,
    /// <c>MSSQL</c> or <c>TS</c>) connects to: <c>{BIRKO_{prefix}_DB or birkoview}_{Tag}</c>, created on first call.
    /// </summary>
    public static string Name(string prefix)
    {
        var configured = Environment.GetEnvironmentVariable($"BIRKO_{prefix}_DB") ?? "birkoview";
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable($"BIRKO_{prefix}_HOST")))
            return configured;
        return Servers.GetOrAdd(prefix, p => new Lazy<Server>(() => Server.Open(p, configured), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value.RunDatabase;
    }

    /// <summary>
    /// A database (or, on MySQL, a schema — the same thing there) a test creates itself beside the run database on the
    /// server named by <paramref name="prefix"/>: <c>{baseName}_{Tag}</c>. It is dropped with the run database
    /// whether or not the test drops it.
    /// </summary>
    public static string RunName(string prefix, string baseName)
    {
        Name(prefix);
        return $"{baseName}_{Tag}";
    }

    /// <summary>
    /// A database for ONE test class, beside the run database: <c>{Name(prefix)}_{suffix}</c>, created on first call
    /// (the run database is not, so it costs a class that does not ask for one nothing).
    /// </summary>
    /// <remarks>
    /// For a class that must not share a <b>connector</b> with its siblings. <c>DataBase.GetConnector</c> caches one per
    /// (type, settings id) for the process, the id is host, port, database and user, and
    /// <c>AbstractConnector.SchemaGeneration</c> lives on it: a sibling that drops a table under an initialised store
    /// bumps it (TASK-288's healing, correct in production) and every store on that connector re-ensures its schema.
    /// A class asserting that the write after a drop FAILS loses that failure whenever a sibling bumps in between. Another
    /// database is another settings id, so such a class is immune by construction — TASK-276's fix for the SQL Server
    /// residue class, and the reason it rejected a shared xUnit collection.
    /// </remarks>
    public static string Dedicated(string prefix, string suffix)
    {
        var run = Name(prefix);
        var name = $"{run}_{suffix}";
        if (!Servers.TryGetValue(prefix, out var server))
            return name; // no live server: Name returned the configured name and nothing is created
        return Dedicateds.GetOrAdd(name, n => new Lazy<string>(() =>
        {
            server.Value.CreateIfAbsent(n);
            return n;
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static readonly ConcurrentDictionary<string, Lazy<string>> Dedicateds = new(StringComparer.Ordinal);

    private enum Kind { PostgreSql, MySql, MSSql }

    private sealed class Server
    {
        private readonly string _prefix;
        private readonly Kind _kind;
        private readonly string _host;
        private readonly int _port;
        private readonly string _user;
        private readonly string _password;
        private readonly string _adminDatabase;

        public string RunDatabase { get; }

        private Server(string prefix, string configured)
        {
            _prefix = prefix;
            (_kind, int defaultPort, string defaultUser, string defaultPassword) = prefix switch
            {
                "PG" or "TS" => (Kind.PostgreSql, 5432, "postgres", "postgres"),
                "MYSQL" => (Kind.MySql, 3306, "root", "root"),
                "MSSQL" => (Kind.MSSql, 1433, "sa", "Birko!Passw0rd"),
                _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Expected PG, MYSQL, MSSQL or TS."),
            };
            _host = Environment.GetEnvironmentVariable($"BIRKO_{prefix}_HOST")!;
            _port = int.TryParse(Environment.GetEnvironmentVariable($"BIRKO_{prefix}_PORT"), out var port) ? port : defaultPort;
            _user = Environment.GetEnvironmentVariable($"BIRKO_{prefix}_USER") ?? defaultUser;
            _password = Environment.GetEnvironmentVariable($"BIRKO_{prefix}_PASSWORD") ?? defaultPassword;
            // PostgreSQL needs SOME database to connect to; the configured one is what the workflow creates.
            _adminDatabase = _kind switch { Kind.MSSql => "master", Kind.PostgreSql => configured, _ => "" };
            RunDatabase = $"{configured}_{Tag}";
        }

        public static Server Open(string prefix, string configured)
        {
            var server = new Server(prefix, configured);
            // Housekeeping for another run's leftovers must not cost this run its database.
            try { server.DropAbandoned(); } catch (DbException) { }
            server.Execute(server._adminDatabase, $"CREATE DATABASE {server.Quote(server.RunDatabase)}");
            Created.Enqueue(server);
            if (prefix == "TS")
                server.Execute(server.RunDatabase, "CREATE EXTENSION IF NOT EXISTS timescaledb");
            return server;
        }

        public void CreateIfAbsent(string database)
        {
            if (ListDatabases().Contains(database, StringComparer.Ordinal)) return;
            Execute(_adminDatabase, $"CREATE DATABASE {Quote(database)}");
            if (_prefix == "TS")
                Execute(database, "CREATE EXTENSION IF NOT EXISTS timescaledb");
        }

        /// <summary>Drops every database on this server that carries this run's <see cref="Tag"/>.</summary>
        public void DropOwn() => Drop(ListDatabases().Where(d => d.Contains(Tag, StringComparison.Ordinal)));

        private void DropAbandoned()
        {
            var cutoff = DateTime.UtcNow - AbandonedAfter;
            Drop(ListDatabases().Where(d =>
            {
                var match = Tagged.Match(d);
                return match.Success
                    && DateTime.TryParseExact(match.Groups[1].Value, "yyMMddHHmmss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var started)
                    && started < cutoff;
            }));
        }

        private void Drop(IEnumerable<string> databases)
        {
            var failures = new List<Exception>();
            foreach (var database in databases.ToList())
            {
                try { DropOne(database); }
                catch (DbException ex) { failures.Add(ex); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        private void DropOne(string database) => Execute(_adminDatabase, _kind switch
        {
            Kind.PostgreSql => $"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)",
            Kind.MSSql => $"IF DB_ID(N'{database.Replace("'", "''")}') IS NOT NULL BEGIN "
                        + $"ALTER DATABASE {Quote(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                        + $"DROP DATABASE {Quote(database)}; END",
            _ => $"DROP DATABASE IF EXISTS {Quote(database)}",
        });

        private List<string> ListDatabases()
        {
            var sql = _kind switch
            {
                Kind.PostgreSql => "SELECT datname FROM pg_database",
                Kind.MSSql => "SELECT name FROM sys.databases",
                _ => "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA",
            };
            var names = new List<string>();
            using var connection = Connect(_adminDatabase);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(0));
            return names;
        }

        private void Execute(string database, string sql)
        {
            using var connection = Connect(database);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            command.ExecuteNonQuery();
        }

        private string Quote(string name) => _kind switch
        {
            Kind.PostgreSql => "\"" + name.Replace("\"", "\"\"") + "\"",
            Kind.MSSql => "[" + name.Replace("]", "]]") + "]",
            _ => "`" + name.Replace("`", "``") + "`",
        };

        private DbConnection Connect(string database)
        {
            var builder = new DbConnectionStringBuilder();
            switch (_kind)
            {
                case Kind.PostgreSql:
                    builder["Host"] = _host;
                    builder["Port"] = _port;
                    builder["Username"] = _user;
                    builder["Password"] = _password;
                    builder["Database"] = database;
                    break;
                case Kind.MySql:
                    builder["Server"] = _host;
                    builder["Port"] = _port;
                    builder["User ID"] = _user;
                    builder["Password"] = _password;
                    if (database.Length > 0) builder["Database"] = database;
                    break;
                default:
                    builder["Server"] = $"{_host},{_port}";
                    builder["User ID"] = _user;
                    builder["Password"] = _password;
                    builder["Database"] = database;
                    builder["TrustServerCertificate"] = true;
                    break;
            }
            // Unpooled: an admin connection must not linger in the pool of the database it is about to drop.
            builder["Pooling"] = false;
            var connection = (DbConnection)Activator.CreateInstance(DriverType())!;
            connection.ConnectionString = builder.ConnectionString;
            connection.Open();
            return connection;
        }

        private Type DriverType()
        {
            var name = _kind switch
            {
                Kind.PostgreSql => "Npgsql.NpgsqlConnection, Npgsql",
                Kind.MySql => "MySqlConnector.MySqlConnection, MySqlConnector",
                _ => "Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient",
            };
            return Type.GetType(name, throwOnError: false)
                ?? throw new InvalidOperationException(
                    $"BIRKO_{_prefix}_HOST is set but this test project does not reference the driver '{name}'.");
        }

        /// <summary>Closes the driver's pooled connections, so a database the run used can be dropped.</summary>
        public void ClearPools() => DriverType().GetMethod("ClearAllPools", Type.EmptyTypes)?.Invoke(null, null);
    }

    /// <summary>
    /// Drops every database this run created. Called by <see cref="LiveTestFramework"/> once every test in the assembly
    /// has finished — the only moment clearing a driver's pools is safe, because before it the clear would close a parallel
    /// sibling's connection (rule 61). A failure never fails a test; it is reported, and a later run sweeps the leftover.
    /// </summary>
    internal static void DropRunDatabases(Action<string> report)
    {
        while (Created.TryDequeue(out var server))
        {
            try
            {
                server.ClearPools();
                server.DropOwn();
            }
            catch (Exception ex)
            {
                report($"LiveDatabase: could not drop the databases of run {Tag}: {ex.Message}");
            }
        }
    }
}

/// <summary>
/// The xUnit test framework every live SQL test project runs under (registered by <c>LiveDatabase.props</c>): xUnit's
/// own, with one step added after the assembly's last test and before it reports itself finished — dropping this run's
/// databases.
/// </summary>
/// <remarks>
/// Two later moments were tried and measured not to work, both because vstest kills the testhost a moment after the
/// assembly reports itself finished: an <see cref="AppDomain.ProcessExit"/> handler, and a disposal registered on the
/// framework (xUnit disposes the framework after that report). Each started, cleared the pools, and was killed before its
/// <c>DROP DATABASE</c> returned, so every run left its database behind. <c>BeforeTestAssemblyFinishedAsync</c> runs
/// before the report, so the runner waits for it.
/// </remarks>
public sealed class LiveTestFramework : Xunit.Sdk.XunitTestFramework
{
    public LiveTestFramework(Xunit.Abstractions.IMessageSink messageSink) : base(messageSink) { }

    protected override Xunit.Abstractions.ITestFrameworkExecutor CreateExecutor(System.Reflection.AssemblyName assemblyName)
        => new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor : Xunit.Sdk.XunitTestFrameworkExecutor
    {
        public Executor(System.Reflection.AssemblyName assemblyName, Xunit.Abstractions.ISourceInformationProvider sourceInformationProvider,
            Xunit.Abstractions.IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink) { }

        // The base body, with the runner swapped for one that tears down.
        protected override async void RunTestCases(IEnumerable<Xunit.Sdk.IXunitTestCase> testCases,
            Xunit.Abstractions.IMessageSink executionMessageSink, Xunit.Abstractions.ITestFrameworkExecutionOptions executionOptions)
        {
            using var runner = new Runner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class Runner : Xunit.Sdk.XunitTestAssemblyRunner
    {
        public Runner(Xunit.Abstractions.ITestAssembly testAssembly, IEnumerable<Xunit.Sdk.IXunitTestCase> testCases,
            Xunit.Abstractions.IMessageSink diagnosticMessageSink, Xunit.Abstractions.IMessageSink executionMessageSink,
            Xunit.Abstractions.ITestFrameworkExecutionOptions executionOptions)
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions) { }

        protected override System.Threading.Tasks.Task BeforeTestAssemblyFinishedAsync()
        {
            LiveDatabase.DropRunDatabases(message =>
            {
                Console.Error.WriteLine(message);
                DiagnosticMessageSink.OnMessage(new Xunit.Sdk.DiagnosticMessage(message));
            });
            return base.BeforeTestAssemblyFinishedAsync();
        }
    }
}
