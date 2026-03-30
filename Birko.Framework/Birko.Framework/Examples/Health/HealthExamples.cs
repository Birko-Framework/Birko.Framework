using System;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Azure;
using Birko.Health.Checks;
using Birko.Health.Data;
using Birko.Security.AzureKeyVault;
using Birko.Storage.AzureBlob;

namespace Birko.Framework.Examples.Health
{
    public static class HealthExamples
    {
        /// <summary>
        /// Core types: HealthCheckResult, HealthStatus.
        /// </summary>
        public static void RunCoreTypesExample()
        {
            ExampleOutput.WriteLine("=== Health Check Core Types ===\n");

            // HealthStatus enum
            ExampleOutput.WriteHeader("HealthStatus");
            ExampleOutput.WriteInfo("Healthy", ((int)HealthStatus.Healthy).ToString());
            ExampleOutput.WriteInfo("Degraded", ((int)HealthStatus.Degraded).ToString());
            ExampleOutput.WriteInfo("Unhealthy", ((int)HealthStatus.Unhealthy).ToString());

            // HealthCheckResult factories
            ExampleOutput.WriteHeader("HealthCheckResult");
            var healthy = HealthCheckResult.Healthy("All systems operational");
            PrintResult("Healthy", healthy);

            var degraded = HealthCheckResult.Degraded("High latency detected");
            PrintResult("Degraded", degraded);

            var ex = new TimeoutException("Connection timed out");
            var unhealthy = HealthCheckResult.Unhealthy("Database unreachable", ex);
            PrintResult("Unhealthy", unhealthy);
            ExampleOutput.WriteInfo("Exception", unhealthy.Exception?.GetType().Name ?? "none");

            // With data
            ExampleOutput.WriteHeader("With Data Dictionary");
            var data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["latencyMs"] = 42.5,
                ["connections"] = 10
            };
            var withData = HealthCheckResult.Healthy("DB OK", data);
            PrintResult("With data", withData);
            foreach (var kv in withData.Data!)
            {
                ExampleOutput.WriteDim($"  {kv.Key} = {kv.Value}");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Built-in system health checks.
        /// </summary>
        public static async Task RunSystemChecksExample()
        {
            ExampleOutput.WriteLine("=== System Health Checks ===\n");

            // Disk space
            ExampleOutput.WriteHeader("Disk Space Check");
            var diskCheck = new DiskSpaceHealthCheck(AppContext.BaseDirectory);
            var diskResult = await diskCheck.CheckAsync();
            PrintResult("Disk", diskResult);
            if (diskResult.Data != null)
            {
                foreach (var kv in diskResult.Data)
                {
                    ExampleOutput.WriteDim($"  {kv.Key} = {kv.Value}");
                }
            }

            // Memory
            ExampleOutput.WriteHeader("Memory Check");
            var memCheck = new MemoryHealthCheck();
            var memResult = await memCheck.CheckAsync();
            PrintResult("Memory", memResult);
            if (memResult.Data != null)
            {
                foreach (var kv in memResult.Data)
                {
                    ExampleOutput.WriteDim($"  {kv.Key} = {kv.Value}");
                }
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// HealthCheckRunner: register checks, run with tags, view aggregated report.
        /// </summary>
        public static async Task RunRunnerExample()
        {
            ExampleOutput.WriteLine("=== Health Check Runner ===\n");

            var runner = new HealthCheckRunner(defaultTimeout: TimeSpan.FromSeconds(5))
                .Register("disk", new DiskSpaceHealthCheck(AppContext.BaseDirectory), "system", "live")
                .Register("memory", new MemoryHealthCheck(), "system", "live")
                .Register("custom-ok", new LambdaCheck(() => HealthCheckResult.Healthy("Custom OK")), "app")
                .Register("custom-slow", new LambdaCheck(() => HealthCheckResult.Degraded("Responding slowly")), "app");

            // Run all
            ExampleOutput.WriteHeader("All Checks");
            var report = await runner.RunAsync();
            PrintReport(report);

            // Run by tag
            ExampleOutput.WriteHeader("Tag: 'system' Only");
            var systemReport = await runner.RunAsync(tag: "system");
            PrintReport(systemReport);

            ExampleOutput.WriteHeader("Tag: 'app' Only");
            var appReport = await runner.RunAsync(tag: "app");
            PrintReport(appReport);

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Registration options: tags, timeout, fluent API.
        /// </summary>
        public static void RunRegistrationExample()
        {
            ExampleOutput.WriteLine("=== Health Check Registration ===\n");

            // Basic registration
            ExampleOutput.WriteHeader("Registration Properties");
            var reg = new HealthCheckRegistration(
                name: "sql-primary",
                factory: () => new LambdaCheck(() => HealthCheckResult.Healthy()),
                tags: new[] { "db", "ready", "live" },
                timeout: TimeSpan.FromSeconds(3),
                timeoutStatus: HealthStatus.Degraded);

            ExampleOutput.WriteInfo("Name", reg.Name);
            ExampleOutput.WriteInfo("Tags", string.Join(", ", reg.Tags));
            ExampleOutput.WriteInfo("Timeout", $"{reg.Timeout?.TotalSeconds}s");
            ExampleOutput.WriteInfo("TimeoutStatus", reg.TimeoutStatus.ToString());

            // Fluent runner
            ExampleOutput.WriteHeader("Fluent Runner API");
            var runner = new HealthCheckRunner()
                .Register("a", new LambdaCheck(() => HealthCheckResult.Healthy()), "live")
                .Register("b", () => new LambdaCheck(() => HealthCheckResult.Healthy()), "ready");

            ExampleOutput.WriteInfo("Registered", runner.Registrations.Count.ToString());
            foreach (var r in runner.Registrations)
            {
                ExampleOutput.WriteDim($"  {r.Name} [{string.Join(", ", r.Tags)}]");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Data health checks: SQL, Elasticsearch, MongoDB, RavenDB, InfluxDB, Vault, MQTT, SMTP.
        /// </summary>
        public static async Task RunDataChecksExample()
        {
            ExampleOutput.WriteLine("=== Data & Infrastructure Health Checks ===\n");

            // SQL — uses a DbConnection factory (demo with unreachable server)
            ExampleOutput.WriteHeader("SqlHealthCheck");
            ExampleOutput.WriteLine("  Takes Func<DbConnection> + optional query (default: SELECT 1)");
            ExampleOutput.WriteDim("  Works with any ADO.NET provider: MSSql, PostgreSQL, MySQL, SQLite, TimescaleDB");
            ExampleOutput.WriteLine("  Example: new SqlHealthCheck(() => new NpgsqlConnection(connStr))");
            ExampleOutput.WriteLine("");

            // Elasticsearch — cluster health API
            ExampleOutput.WriteHeader("ElasticSearchHealthCheck");
            ExampleOutput.WriteLine("  Calls /_cluster/health API");
            ExampleOutput.WriteDim("  Maps: green=Healthy, yellow=Degraded, red=Unhealthy");
            var esCheck = new ElasticSearchHealthCheck("http://localhost:9200");
            var esResult = await esCheck.CheckAsync();
            PrintResult("ES (localhost:9200)", esResult);

            // MongoDB — custom ping or TCP
            ExampleOutput.WriteHeader("MongoDbHealthCheck");
            ExampleOutput.WriteLine("  Mode 1: Custom ping function (for MongoDB driver)");
            ExampleOutput.WriteDim("    new MongoDbHealthCheck(async ct => { await client.Ping(); return true; })");
            ExampleOutput.WriteLine("  Mode 2: Simple TCP connect");
            ExampleOutput.WriteDim("    new MongoDbHealthCheck(\"mongo-host\", 27017)");
            var mongoCheck = new MongoDbHealthCheck(ct => Task.FromResult(true), "MongoDB (mock)");
            var mongoResult = await mongoCheck.CheckAsync();
            PrintResult("MongoDB (mock ping)", mongoResult);

            // RavenDB — /build/version endpoint
            ExampleOutput.WriteHeader("RavenDbHealthCheck");
            ExampleOutput.WriteLine("  Calls /build/version endpoint");
            var ravenCheck = new RavenDbHealthCheck("http://localhost:8080");
            var ravenResult = await ravenCheck.CheckAsync();
            PrintResult("RavenDB (localhost:8080)", ravenResult);

            // InfluxDB — /ping endpoint
            ExampleOutput.WriteHeader("InfluxDbHealthCheck");
            ExampleOutput.WriteLine("  Calls /ping endpoint, measures latency");
            ExampleOutput.WriteDim("  new InfluxDbHealthCheck(\"http://localhost:8086\")");
            var influxCheck = new InfluxDbHealthCheck("http://localhost:8086");
            var influxResult = await influxCheck.CheckAsync();
            PrintResult("InfluxDB (localhost:8086)", influxResult);

            // Vault — /v1/sys/health endpoint
            ExampleOutput.WriteHeader("VaultHealthCheck");
            ExampleOutput.WriteLine("  Calls /v1/sys/health endpoint");
            ExampleOutput.WriteDim("  200=Healthy, 429/473=Degraded (standby), 501/503=Unhealthy");
            var vaultCheck = new VaultHealthCheck("http://localhost:8200");
            var vaultResult = await vaultCheck.CheckAsync();
            PrintResult("Vault (localhost:8200)", vaultResult);

            // MQTT — TCP or custom ping
            ExampleOutput.WriteHeader("MqttHealthCheck");
            ExampleOutput.WriteLine("  Mode 1: TCP connect to broker port");
            ExampleOutput.WriteDim("    new MqttHealthCheck(\"mqtt-broker\", 1883)");
            ExampleOutput.WriteLine("  Mode 2: Custom ping (e.g., mqttQueue.IsConnected)");
            ExampleOutput.WriteDim("    new MqttHealthCheck(ct => Task.FromResult(queue.IsConnected))");
            var mqttCheck = new MqttHealthCheck(ct => Task.FromResult(true), "MQTT (mock)");
            var mqttResult = await mqttCheck.CheckAsync();
            PrintResult("MQTT (mock ping)", mqttResult);

            // SMTP — TCP + banner check
            ExampleOutput.WriteHeader("SmtpHealthCheck");
            ExampleOutput.WriteLine("  TCP connect, reads SMTP 220 banner, sends QUIT");
            ExampleOutput.WriteDim("  new SmtpHealthCheck(\"smtp.example.com\", 587)");
            ExampleOutput.WriteDim("  Returns: host, port, latencyMs, banner");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Redis health check: PING + latency measurement.
        /// </summary>
        public static void RunRedisCheckExample()
        {
            ExampleOutput.WriteLine("=== Redis Health Check ===\n");

            ExampleOutput.WriteHeader("RedisHealthCheck");
            ExampleOutput.WriteLine("  Sends PING command, measures latency");
            ExampleOutput.WriteDim("  Healthy: connected + latency < 100ms");
            ExampleOutput.WriteDim("  Degraded: connected but latency > 100ms");
            ExampleOutput.WriteDim("  Unhealthy: not connected or exception");
            ExampleOutput.WriteLine("");

            ExampleOutput.WriteHeader("Usage");
            ExampleOutput.WriteLine("  // From existing connection multiplexer");
            ExampleOutput.WriteDim("  var check = new RedisHealthCheck(connectionMultiplexer);");
            ExampleOutput.WriteLine("");
            ExampleOutput.WriteLine("  // From factory (lazy connection)");
            ExampleOutput.WriteDim("  var check = new RedisHealthCheck(() => manager.GetConnection());");
            ExampleOutput.WriteLine("");
            ExampleOutput.WriteInfo("Returns", "latencyMs, isConnected in Data dictionary");

            ExampleOutput.WriteLine("\nNote: Live Redis demo requires a running Redis server.");
            ExampleOutput.WriteDim("  Register with runner: runner.Register(\"redis\", check, \"cache\", \"ready\")");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Full-stack example: system + data checks aggregated with tag filtering.
        /// </summary>
        public static async Task RunFullStackExample()
        {
            ExampleOutput.WriteLine("=== Full Stack Health Check (Symbio-like) ===\n");

            ExampleOutput.WriteHeader("Scenario: all backends + Azure + system checks");
            ExampleOutput.WriteDim("  Like Symbio: SQL + MongoDB + ES + RavenDB + InfluxDB + Redis + MQTT + Vault + Azure + SMTP + Disk + Memory");
            ExampleOutput.WriteLine("");

            // Build a runner simulating a real multi-backend app
            var runner = new HealthCheckRunner(defaultTimeout: TimeSpan.FromSeconds(5))
                // System
                .Register("disk", new DiskSpaceHealthCheck(AppContext.BaseDirectory), "system", "live")
                .Register("memory", new MemoryHealthCheck(), "system", "live")
                // Simulated DB checks
                .Register("sql-primary", new LambdaCheck(() => HealthCheckResult.Healthy("PostgreSQL OK")), "db", "ready")
                .Register("mongodb", new LambdaCheck(() => HealthCheckResult.Healthy("MongoDB OK")), "db", "ready")
                .Register("elasticsearch", new LambdaCheck(() => HealthCheckResult.Degraded("ES cluster yellow")), "db")
                .Register("ravendb", new LambdaCheck(() => HealthCheckResult.Healthy("RavenDB OK")), "db")
                // Cache
                .Register("redis", new LambdaCheck(() => HealthCheckResult.Healthy("Redis OK (2.1ms)")), "cache", "ready")
                // Time-series
                .Register("influxdb", new LambdaCheck(() => HealthCheckResult.Healthy("InfluxDB OK (15ms)")), "db")
                // Messaging
                .Register("mqtt", new LambdaCheck(() => HealthCheckResult.Healthy("MQTT OK (8ms)")), "messaging", "ready")
                .Register("smtp", new LambdaCheck(() => HealthCheckResult.Healthy("SMTP OK (95ms)")), "messaging")
                // Secrets
                .Register("vault", new LambdaCheck(() => HealthCheckResult.Healthy("Vault OK (50ms)")), "secrets")
                // Azure
                .Register("azure-blob", new LambdaCheck(() => HealthCheckResult.Healthy("Azure Blob OK (45ms)")), "azure", "storage", "ready")
                .Register("azure-keyvault", new LambdaCheck(() => HealthCheckResult.Healthy("Azure KV OK (120ms)")), "azure", "secrets");

            // /health — all checks
            ExampleOutput.WriteHeader("GET /health (all checks)");
            var all = await runner.RunAsync();
            PrintReport(all);

            // /health/ready — readiness probe (db + cache)
            ExampleOutput.WriteHeader("GET /health/ready (tag: ready)");
            var ready = await runner.RunAsync(tag: "ready");
            PrintReport(ready);

            // /health/live — liveness probe (system only)
            ExampleOutput.WriteHeader("GET /health/live (tag: live)");
            var live = await runner.RunAsync(tag: "live");
            PrintReport(live);

            // /health?tag=db — database only
            ExampleOutput.WriteHeader("GET /health?tag=db (databases only)");
            var db = await runner.RunAsync(tag: "db");
            PrintReport(db);

            ExampleOutput.WriteHeader("ASP.NET Core Integration");
            ExampleOutput.WriteDim("  app.MapGet(\"/health\", async () => {");
            ExampleOutput.WriteDim("      var report = await runner.RunAsync();");
            ExampleOutput.WriteDim("      return Results.Json(report, statusCode: report.Status == HealthStatus.Healthy ? 200 : 503);");
            ExampleOutput.WriteDim("  });");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Azure health checks: Blob Storage and Key Vault.
        /// </summary>
        public static void RunAzureChecksExample()
        {
            ExampleOutput.WriteLine("=== Azure Health Checks ===\n");

            // Azure Blob Storage
            ExampleOutput.WriteHeader("AzureBlobHealthCheck");
            ExampleOutput.WriteLine("  Probes Azure Blob Storage by listing blobs (maxResults=1)");
            ExampleOutput.WriteDim("  Healthy: responds within 2s");
            ExampleOutput.WriteDim("  Degraded: responds but slower than 2s");
            ExampleOutput.WriteDim("  Unhealthy: connection failed or exception");
            ExampleOutput.WriteLine("");

            ExampleOutput.WriteHeader("Usage");
            ExampleOutput.WriteDim("  // From existing AzureBlobStorage instance");
            ExampleOutput.WriteDim("  var check = new AzureBlobHealthCheck(storage);");
            ExampleOutput.WriteDim("  // From factory (DI-friendly)");
            ExampleOutput.WriteDim("  var check = new AzureBlobHealthCheck(() => sp.GetRequiredService<AzureBlobStorage>());");
            ExampleOutput.WriteInfo("Returns", "latencyMs in Data dictionary");
            ExampleOutput.WriteLine("");

            // Azure Key Vault
            ExampleOutput.WriteHeader("AzureKeyVaultHealthCheck");
            ExampleOutput.WriteLine("  Probes Azure Key Vault by listing secrets");
            ExampleOutput.WriteDim("  Same three-level status: Healthy / Degraded / Unhealthy");
            ExampleOutput.WriteDim("  Same latency threshold (2s) and data dictionary pattern");
            ExampleOutput.WriteLine("");

            ExampleOutput.WriteHeader("Usage");
            ExampleOutput.WriteDim("  var check = new AzureKeyVaultHealthCheck(secretProvider);");
            ExampleOutput.WriteDim("  var check = new AzureKeyVaultHealthCheck(() => sp.GetRequiredService<AzureKeyVaultSecretProvider>());");
            ExampleOutput.WriteLine("");

            // Registration
            ExampleOutput.WriteHeader("Registration with Runner");
            ExampleOutput.WriteDim("  runner.Register(\"azure-blob\", new AzureBlobHealthCheck(storage), \"azure\", \"storage\", \"ready\");");
            ExampleOutput.WriteDim("  runner.Register(\"azure-keyvault\", new AzureKeyVaultHealthCheck(provider), \"azure\", \"secrets\");");
            ExampleOutput.WriteDim("  var azureReport = await runner.RunAsync(tag: \"azure\");");

            ExampleOutput.WriteLine("\nNote: Live Azure demo requires a running Azure subscription.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        // ── Helpers ──

        private static void PrintResult(string label, HealthCheckResult result)
        {
            var icon = result.Status switch
            {
                HealthStatus.Healthy => "OK",
                HealthStatus.Degraded => "WARN",
                _ => "FAIL"
            };

            if (result.Status == HealthStatus.Healthy)
                ExampleOutput.WriteSuccess($"{label}: {icon} — {result.Description}");
            else if (result.Status == HealthStatus.Degraded)
                ExampleOutput.WriteInfo($"{label}", $"{icon} — {result.Description}");
            else
                ExampleOutput.WriteError($"{label}: {icon} — {result.Description}");
        }

        private static void PrintReport(HealthReport report)
        {
            var statusLabel = report.Status switch
            {
                HealthStatus.Healthy => "HEALTHY",
                HealthStatus.Degraded => "DEGRADED",
                _ => "UNHEALTHY"
            };
            ExampleOutput.WriteInfo("Overall", $"{statusLabel} ({report.TotalDuration.TotalMilliseconds:F0}ms)");

            foreach (var (name, result) in report.Entries)
            {
                var dur = result.Duration.TotalMilliseconds > 0 ? $" {result.Duration.TotalMilliseconds:F0}ms" : "";
                ExampleOutput.WriteDim($"  {name}: {result.Status}{dur} — {result.Description}");
            }
        }

        private class LambdaCheck : IHealthCheck
        {
            private readonly Func<HealthCheckResult> _func;
            public LambdaCheck(Func<HealthCheckResult> func) => _func = func;
            public System.Threading.Tasks.Task<HealthCheckResult> CheckAsync(System.Threading.CancellationToken ct = default)
                => System.Threading.Tasks.Task.FromResult(_func());
        }
    }
}
