using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;
using Birko.Framework.Configuration;
using Birko.Framework.Services;
using Birko.Framework.Examples.Communication;
using Birko.Framework.Examples.Data;
using Birko.Framework.Examples.Models;
using Birko.Framework.Examples.Validation;
using Birko.Framework.Examples.Caching;
using Birko.Framework.Examples.Security;
using Birko.Framework.Examples.BackgroundJobs;
using Birko.Framework.Examples.MessageQueue;
using Birko.Framework.Examples.EventBus;
using Birko.Framework.Examples.Messaging;
using Birko.Framework.Examples.Telemetry;
using Birko.Framework.Examples.Rules;
using Birko.Framework.Examples.Processors;
using Birko.Framework.Examples.Health;
using Birko.Framework.Examples.Serialization;
using Birko.Framework.Examples.Storage;

namespace Birko.Framework
{
    class Program
    {
        private static LogControl _log = null!;
        private static bool _isRunning;
        private static IServiceProvider _serviceProvider = null!;

        static async Task Main(string[] args)
        {
            try
            {
                var configuration = BuildConfiguration();
                var services = ConfigureServices(configuration);
                _serviceProvider = services.BuildServiceProvider();
                var bootstrap = await _serviceProvider.InitializeFrameworkAsync();

                try
                {
                    new TerminalApp(BuildMainUI()).Run();
                }
                finally
                {
                    await bootstrap.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nFatal Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        static IConfiguration BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<Program>(optional: true)
                .AddEnvironmentVariables()
                .Build();
        }

        static IServiceCollection ConfigureServices(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddBirkoFramework(configuration);
            return services;
        }

        // ────────────────────────────────────────────────────────
        //  UI Construction
        // ────────────────────────────────────────────────────────

        static Visual BuildMainUI()
        {
            _log = new LogControl();
            Examples.ExampleOutput.SetTarget(_log);
            _log.AppendMarkupLine("[bold cyan]Birko Framework[/] Interactive Demo");
            _log.AppendLine("");
            _log.AppendLine("Select an example from the left panel and click to run it.");
            _log.AppendLine("Output will appear here. Use Ctrl+F to search output.");

            var navigation = new TabControl(
                BuildDataTab(),
                BuildCommunicationTab(),
                BuildModelsTab(),
                BuildValidationTab(),
                BuildCachingTab(),
                BuildSerializationTab(),
                BuildSecurityTab(),
                BuildBackgroundJobsTab(),
                BuildMessageQueueTab(),
                BuildEventBusTab(),
                BuildMessagingTab(),
                BuildTelemetryTab(),
                BuildRulesTab(),
                BuildProcessorsTab(),
                BuildStorageTab(),
                BuildHealthTab(),
                BuildAboutTab()
            );

            var outputPanel = new Group(new Markup("[bold]Output[/]"))
                .Content(_log);

            return new DockLayout()
                .Top(new Header()
                    .Left(new Markup("[bold cyan]Birko Framework[/]"))
                    .Right("Interactive Demo"))
                .Bottom(new Footer()
                    .Left("Click: Run Example  |  Ctrl+F: Search Output")
                    .Right("Ctrl+Q: Exit"))
                .Content(
                    new HSplitter(
                        new Group(new Markup("[bold]Examples[/]")).Content(navigation),
                        outputPanel
                    ).Ratio(0.35).MinFirst(25).MinSecond(40)
                );
        }

        // ────────────────────────────────────────────────────────
        //  Example Runner
        // ────────────────────────────────────────────────────────

        static void RunExample(string name, string key)
        {
            if (_isRunning) return;
            _isRunning = true;

            _log.Clear();
            _log.AppendMarkupLine($"[bold cyan]▶ {name}[/]");
            _log.AppendLine(new string('─', 50));
            _log.AppendLine("");

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteExample(key);
                }
                catch (Exception ex)
                {
                    var msg = EscapeMarkup(ex.Message);
                    _log.Dispatcher.Post(() => _log.AppendMarkupLine($"\n[bold red]Error:[/] {msg}"));
                }
                finally
                {
                    _log.Dispatcher.Post(() =>
                    {
                        _log.AppendLine("");
                        _log.AppendLine(new string('─', 50));
                        _log.AppendMarkupLine("[dim]Done. Select another example to run.[/]");
                        _log.ScrollToTail();
                    });
                    _isRunning = false;
                }
            });
        }

        static Button ExampleButton(string label, string key)
        {
            return new Button($" ▸ {label} ")
                .Click(() => RunExample(label, key));
        }

        static string EscapeMarkup(string text)
        {
            return text.Replace("[", "[[").Replace("]", "]]");
        }

        // ────────────────────────────────────────────────────────
        //  Tab Builders
        // ────────────────────────────────────────────────────────

        static TabPage BuildDataTab()
        {
            return new TabPage("Data", new ScrollViewer(new VStack(
                ExampleGroup("SQL", "Database operations with SQL connectors",
                    ExampleButton("Basic CRUD", "sql-crud"),
                    ExampleButton("Async Operations", "sql-async"),
                    ExampleButton("Bulk Operations", "sql-bulk"),
                    ExampleButton("Provider Configuration", "sql-config")
                ),
                ExampleGroup("MongoDB", "Document database operations",
                    ExampleButton("Basic CRUD", "mongo-crud"),
                    ExampleButton("Bulk Operations", "mongo-bulk"),
                    ExampleButton("Connection Configuration", "mongo-config")
                ),
                ExampleGroup("ElasticSearch", "Search engine operations",
                    ExampleButton("Basic CRUD", "es-crud"),
                    ExampleButton("Streaming", "es-streaming"),
                    ExampleButton("Health Check", "es-health")
                ),
                ExampleGroup("JSON Store", "File-based storage",
                    ExampleButton("Basic CRUD", "json-crud"),
                    ExampleButton("Multi-Store", "json-multi"),
                    ExampleButton("Prototyping", "json-proto")
                ),
                ExampleGroup("Data Sync", "Cross-store synchronization",
                    ExampleButton("Basic Sync", "sync-basic"),
                    ExampleButton("Preview Sync", "sync-preview"),
                    ExampleButton("Multi-Tenant Sync", "sync-multi-tenant"),
                    ExampleButton("Conflict Resolution", "sync-conflict"),
                    ExampleButton("Sync Queue", "sync-queue"),
                    ExampleButton("Custom Conflict Resolver", "sync-custom"),
                    ExampleButton("Tenant-Aware Sync", "sync-tenant")
                ),
                ExampleGroup("Multi-Tenancy", "Tenant isolation and scoping",
                    ExampleButton("Basic Tenant Context", "tenant-basic"),
                    ExampleButton("Scoped Operations", "tenant-scoped"),
                    ExampleButton("Store Wrapper", "tenant-store"),
                    ExampleButton("Async Scoped Operations", "tenant-async"),
                    ExampleButton("Non-Tenant Mode", "tenant-non-tenant"),
                    ExampleButton("Filtering", "tenant-filter"),
                    ExampleButton("Security", "tenant-security")
                )
            ).Spacing(1)));
        }

        static TabPage BuildCommunicationTab()
        {
            return new TabPage("Comm", new ScrollViewer(new VStack(
                ExampleGroup("Network", "TCP/UDP communication",
                    ExampleButton("TCP Client", "net-tcp"),
                    ExampleButton("UDP Communication", "net-udp"),
                    ExampleButton("Network Info", "net-info")
                ),
                ExampleGroup("Hardware", "Serial/Parallel/IR ports",
                    ExampleButton("Serial Port", "hw-serial"),
                    ExampleButton("Parallel Port (LPT)", "hw-lpt"),
                    ExampleButton("Infrared Port", "hw-ir"),
                    ExampleButton("List Available Ports", "hw-list")
                ),
                ExampleGroup("Bluetooth", "Classic and BLE",
                    ExampleButton("Classic Bluetooth", "bt-classic"),
                    ExampleButton("Bluetooth LE", "bt-le"),
                    ExampleButton("BLE Device Scan", "bt-scan"),
                    ExampleButton("Adapter Info", "bt-info")
                ),
                ExampleGroup("WebSocket", "Real-time bidirectional",
                    ExampleButton("WebSocket Server", "ws-server")
                ),
                ExampleGroup("SOAP", "XML web services",
                    ExampleButton("Run Server", "soap-server"),
                    ExampleButton("Run Client", "soap-client"),
                    ExampleButton("Cached Client", "soap-cached")
                ),
                ExampleGroup("REST", "HTTP API client",
                    ExampleButton("Basic REST Client", "rest-basic")
                ),
                ExampleGroup("SSE", "Server-Sent Events",
                    ExampleButton("Basic Server", "sse-basic"),
                    ExampleButton("Broadcast", "sse-broadcast"),
                    ExampleButton("Notifications", "sse-notify"),
                    ExampleButton("Client", "sse-client"),
                    ExampleButton("Stock Ticker", "sse-ticker"),
                    ExampleButton("Authenticated Server", "sse-auth"),
                    ExampleButton("Middleware Pipeline", "sse-middleware")
                ),
                ExampleGroup("Authentication", "Token-based auth",
                    ExampleButton("REST Server with Logging", "auth-rest-log"),
                    ExampleButton("REST Server with IP-Bound Tokens", "auth-rest-ip"),
                    ExampleButton("REST Client Authentication", "auth-rest-client"),
                    ExampleButton("SOAP Authentication", "auth-soap")
                )
            ).Spacing(1)));
        }

        static TabPage BuildModelsTab()
        {
            return new TabPage("Mod", new ScrollViewer(new VStack(
                ExampleGroup("Product & Category", "Domain model examples",
                    ExampleButton("Product Models", "model-product"),
                    ExampleButton("Categories", "model-category"),
                    ExampleButton("Pricing (Currency/Tax)", "model-pricing"),
                    ExampleButton("Inventory (Warehouse)", "model-inventory")
                )
            ).Spacing(1)));
        }

        static TabPage BuildValidationTab()
        {
            return new TabPage("Val", new ScrollViewer(new VStack(
                ExampleGroup("Fluent Validation", "IValidator<T> with built-in rules",
                    ExampleButton("Basic Validation", "val-basic"),
                    ExampleButton("Custom Rules", "val-custom"),
                    ExampleButton("Async Validation", "val-async")
                )
            ).Spacing(1)));
        }

        static TabPage BuildCachingTab()
        {
            return new TabPage("Cache", new ScrollViewer(new VStack(
                ExampleGroup("Cache Backends", "ICache with Memory and Redis",
                    ExampleButton("Memory Cache", "cache-memory"),
                    ExampleButton("Cache Expiration", "cache-expiration"),
                    ExampleButton("Redis Cache", "cache-redis")
                )
            ).Spacing(1)));
        }

        static TabPage BuildSerializationTab()
        {
            return new TabPage("Ser", new ScrollViewer(new VStack(
                ExampleGroup("Built-in", "System.Text.Json and System.Xml (no dependencies)",
                    ExampleButton("System.Text.Json", "ser-json"),
                    ExampleButton("System.Xml", "ser-xml")
                ),
                ExampleGroup("External", "Third-party serialization libraries",
                    ExampleButton("Newtonsoft.Json", "ser-newtonsoft"),
                    ExampleButton("MessagePack", "ser-msgpack"),
                    ExampleButton("Protobuf", "ser-protobuf")
                ),
                ExampleGroup("Comparison", "Compare all formats",
                    ExampleButton("Format Comparison", "ser-compare")
                )
            ).Spacing(1)));
        }

        static TabPage BuildSecurityTab()
        {
            return new TabPage("Sec", new ScrollViewer(new VStack(
                ExampleGroup("Cryptography", "Hashing and encryption",
                    ExampleButton("Password Hashing (PBKDF2)", "sec-password"),
                    ExampleButton("Encryption (AES-256-GCM)", "sec-encryption")
                ),
                ExampleGroup("Tokens", "Authentication tokens",
                    ExampleButton("JWT Tokens", "sec-jwt")
                ),
                ExampleGroup("ASP.NET Core", "Web application security",
                    ExampleButton("ICurrentUser", "sec-aspnet-user"),
                    ExampleButton("Permission Checker", "sec-aspnet-perms"),
                    ExampleButton("Token Service Adapter", "sec-aspnet-token"),
                    ExampleButton("Tenant Resolvers", "sec-aspnet-tenant"),
                    ExampleButton("Endpoint Filter", "sec-aspnet-filter"),
                    ExampleButton("DI Registration", "sec-aspnet-di")
                )
            ).Spacing(1)));
        }

        static TabPage BuildBackgroundJobsTab()
        {
            return new TabPage("Jobs", new ScrollViewer(new VStack(
                ExampleGroup("Processing", "Queue, dispatch, execute",
                    ExampleButton("Job Dispatcher", "bj-dispatcher"),
                    ExampleButton("Job Processor", "bj-processor"),
                    ExampleButton("Recurring Scheduler", "bj-recurring")
                ),
                ExampleGroup("Configuration", "Retry and options",
                    ExampleButton("Retry Policy", "bj-retry"),
                    ExampleButton("Configuration Overview", "bj-config")
                )
            ).Spacing(1)));
        }

        static TabPage BuildMessageQueueTab()
        {
            return new TabPage("MQ", new ScrollViewer(new VStack(
                ExampleGroup("Pub/Sub", "InMemory message queue",
                    ExampleButton("Basic Pub/Sub", "mq-pubsub"),
                    ExampleButton("Typed Messages", "mq-typed"),
                    ExampleButton("Manual Ack", "mq-ack")
                ),
                ExampleGroup("Serialization", "Message encoding and encryption",
                    ExampleButton("Encrypting Serializer", "mq-encrypt"),
                    ExampleButton("Message Fingerprint", "mq-fingerprint")
                ),
                ExampleGroup("MQTT", "IoT protocol utilities",
                    ExampleButton("Topic Validation & Matching", "mq-mqtt-topics")
                )
            ).Spacing(1)));
        }

        static TabPage BuildEventBusTab()
        {
            return new TabPage("EB", new ScrollViewer(new VStack(
                ExampleGroup("Core", "In-process event bus",
                    ExampleButton("Publish/Subscribe", "eb-inprocess"),
                    ExampleButton("Pipeline Behaviors", "eb-pipeline"),
                    ExampleButton("Deduplication", "eb-dedup"),
                    ExampleButton("Topic Conventions", "eb-topics")
                ),
                ExampleGroup("Distributed", "Event bus over MessageQueue",
                    ExampleButton("InMemory Transport", "eb-distributed")
                ),
                ExampleGroup("Outbox", "Transactional outbox pattern",
                    ExampleButton("Outbox Flow", "eb-outbox")
                )
            ).Spacing(1)));
        }

        static TabPage BuildMessagingTab()
        {
            return new TabPage("Msg", new ScrollViewer(new VStack(
                ExampleGroup("Email", "SMTP email sending",
                    ExampleButton("Email Settings", "msg-settings"),
                    ExampleButton("Email Message", "msg-email"),
                    ExampleButton("SMTP Sender", "msg-smtp")
                ),
                ExampleGroup("Templates", "Message template rendering",
                    ExampleButton("String Templates", "msg-template"),
                    ExampleButton("Razor Inline", "msg-razor-inline"),
                    ExampleButton("Razor Conditionals", "msg-razor-logic"),
                    ExampleButton("Razor File Templates", "msg-razor-file"),
                    ExampleButton("Razor IMessageTemplate", "msg-razor-mt"),
                    ExampleButton("Razor Options", "msg-razor-options"),
                    ExampleButton("Razor Errors", "msg-razor-errors"),
                    ExampleButton("String vs Razor", "msg-razor-compare")
                ),
                ExampleGroup("Core Types", "Addresses, results, SMS, push",
                    ExampleButton("Message Address", "msg-address"),
                    ExampleButton("Message Result", "msg-result"),
                    ExampleButton("SMS & Push Messages", "msg-sms-push")
                )
            ).Spacing(1)));
        }

        static TabPage BuildTelemetryTab()
        {
            return new TabPage("Tel", new ScrollViewer(new VStack(
                ExampleGroup("Store Instrumentation", "Metrics and tracing for store operations",
                    ExampleButton("Sync Store Metrics", "tel-store"),
                    ExampleButton("Async Store Metrics", "tel-async"),
                    ExampleButton("Distributed Tracing", "tel-tracing"),
                    ExampleButton("Error Tracking", "tel-errors")
                ),
                ExampleGroup("Configuration", "Setup and extension methods",
                    ExampleButton("Extension Methods", "tel-extensions"),
                    ExampleButton("Correlation ID Middleware", "tel-correlation")
                )
            ).Spacing(1)));
        }

        static TabPage BuildRulesTab()
        {
            return new TabPage("Rules", new ScrollViewer(new VStack(
                ExampleGroup("Core", "Rule evaluation and contexts",
                    ExampleButton("Basic Rules", "rules-basic"),
                    ExampleButton("Groups & Nesting", "rules-groups"),
                    ExampleButton("Object Context", "rules-object"),
                    ExampleButton("RuleSet Management", "rules-ruleset")
                ),
                ExampleGroup("Integrations", "Rules in other framework components",
                    ExampleButton("SQL Condition Converter", "rules-sql"),
                    ExampleButton("Specification Pattern", "rules-spec"),
                    ExampleButton("Rule-Based Validation", "rules-validation")
                )
            ).Spacing(1)));
        }

        static TabPage BuildProcessorsTab()
        {
            return new TabPage("Proc", new ScrollViewer(new VStack(
                ExampleGroup("Parsing", "CSV and XML stream processors",
                    ExampleButton("CsvParser (Helpers)", "proc-csv-parser"),
                    ExampleButton("CsvProcessor", "proc-csv"),
                    ExampleButton("CsvProcessor (Sync)", "proc-csv-sync"),
                    ExampleButton("XmlProcessor", "proc-xml")
                ),
                ExampleGroup("Transport", "Download and extraction decorators",
                    ExampleButton("ZipProcessor", "proc-zip"),
                    ExampleButton("Composition Pattern", "proc-composition")
                ),
                ExampleGroup("Error Handling", "Exception types",
                    ExampleButton("Error Handling", "proc-errors")
                )
            ).Spacing(1)));
        }

        static TabPage BuildStorageTab()
        {
            return new TabPage("Stor", new ScrollViewer(new VStack(
                ExampleGroup("Core", "Storage types and interfaces",
                    ExampleButton("Core Types", "stor-core"),
                    ExampleButton("Error Handling", "stor-errors")
                ),
                ExampleGroup("Local", "Filesystem storage provider",
                    ExampleButton("Local Storage", "stor-local"),
                    ExampleButton("Tenant Isolation", "stor-tenant")
                ),
                ExampleGroup("Azure", "Azure Blob Storage provider",
                    ExampleButton("Azure Blob", "stor-azure")
                )
            ).Spacing(1)));
        }

        static TabPage BuildHealthTab()
        {
            return new TabPage("Hlth", new ScrollViewer(new VStack(
                ExampleGroup("Core", "Health check types and runner",
                    ExampleButton("Core Types", "hlth-core"),
                    ExampleButton("Registration", "hlth-registration"),
                    ExampleButton("Runner", "hlth-runner")
                ),
                ExampleGroup("System Checks", "Disk and memory monitoring",
                    ExampleButton("System Checks", "hlth-system")
                ),
                ExampleGroup("Data & Redis", "Database and cache health checks",
                    ExampleButton("Data Checks", "hlth-data"),
                    ExampleButton("Redis Check", "hlth-redis"),
                    ExampleButton("Full Stack", "hlth-fullstack")
                ),
                ExampleGroup("Azure", "Azure cloud health checks",
                    ExampleButton("Azure Checks", "hlth-azure")
                )
            ).Spacing(1)));
        }

        static TabPage BuildAboutTab()
        {
            string frameworkVersion, environment;
            try
            {
                var fw = _serviceProvider.GetRequiredService<FrameworkOptions>();
                frameworkVersion = fw.Version;
                environment = fw.Environment;
            }
            catch
            {
                frameworkVersion = "1.0.0";
                environment = "Development";
            }

            return new TabPage("About", new ScrollViewer(new VStack(
                BuildBirkoLogo(),
                "",
                new Markup($"  [bold]A modular .NET framework for enterprise applications[/]"),
                "",
                $"  Version:       {frameworkVersion}",
                $"  Environment:   {environment}",
                $"  .NET Runtime:  {Environment.Version}",
                $"  OS:            {Environment.OSVersion}",
                $"  Machine:       {Environment.MachineName}",
                "",
                new Rule("Storage Backends"),
                "  SQL:     SQL Server, PostgreSQL, MySQL, SQLite",
                "  NoSQL:   MongoDB, RavenDB",
                "  Search:  ElasticSearch",
                "  Time:    InfluxDB, TimescaleDB",
                "  File:    JSON",
                "",
                new Rule("Communication"),
                "  Network, Hardware, Bluetooth, WebSocket, SOAP, REST, SSE",
                "",
                new Rule("Cross-Cutting"),
                "  Validation, Caching (Memory/Redis), Security (PBKDF2/AES/JWT)",
                "  Patterns (UoW/SoftDelete/Audit/Paging), Migrations, Sync",
                "  Background Jobs (SQL/ElasticSearch/MongoDB/RavenDB/JSON)",
                "  Message Queue (InMemory/MQTT), Event Bus (In-Process/Distributed/Outbox)",
                "  Messaging (Email/SMTP, Razor templates, SMS, Push), Storage (Local, Azure Blob)",
                "  Telemetry (Store Metrics/Tracing, Correlation ID)",
                "  Rules Engine (Data-driven rules, SQL/Validation/Spec/EventBus integration)",
                "  Data Processors (XML/CSV/HTTP/ZIP, decorator composition)",
                "  Health Checks (Disk/Memory/SQL/ES/MongoDB/RavenDB/InfluxDB/Redis/MQTT/SMTP/Vault/Azure)",
                "",
                new Rule("Diagnostics"),
                ExampleButton("Run Health Check", "health-check"),
                ExampleButton("Show Configuration", "show-config")
            ).Spacing(0)));
        }

        static Visual BuildBirkoLogo()
        {
            return new VStack(
                new Markup("[bold rgb(56,159,214)]  /\\    /\\    /\\[/]"),
                new Markup("[bold rgb(56,159,214)] /  \\  /  \\  /  \\[/]"),
                new Markup("[rgb(110,198,240)] /    \\/    \\/    \\[/]"),
                new Markup("[rgb(26,95,138)] /  /\\  /\\  /\\  /\\  \\[/]"),
                new Markup("[rgb(26,95,138)] \\____\\/__\\/__\\/____/[/]"),
                "",
                new Markup("[white]      ●   ●[/]"),
                new Markup("[white]     ●     ●[/]"),
                new Markup("[white]      ( ● )[/]"),
                "",
                new Markup("[bold rgb(56,159,214)] B I R K O[/]"),
                new Markup("[rgb(26,95,138)] F R A M E W O R K[/]")
            ).Spacing(0);
        }

        // ────────────────────────────────────────────────────────
        //  Visual Helpers
        // ────────────────────────────────────────────────────────

        static Group ExampleGroup(string title, string description, params Visual[] examples)
        {
            return new Group(new Markup($"[bold cyan] {title} [/]"))
                .Content(new VStack(
                    new Markup($"[dim]{description}[/]"),
                    new VStack(examples)
                ).Spacing(0))
                .BottomRightText(new Markup($"[dim]{examples.Length}[/]"));
        }

        // ────────────────────────────────────────────────────────
        //  Example Execution
        // ────────────────────────────────────────────────────────

        static async Task ExecuteExample(string key)
        {
            switch (key)
            {
                // Data - SQL
                case "sql-crud": await SqlExamples.RunBasicCrudExample(); break;
                case "sql-async": await SqlExamples.RunAsyncOperationsExample(); break;
                case "sql-bulk": await SqlExamples.RunBulkOperationsExample(); break;
                case "sql-config": SqlExamples.ShowProviderConfiguration(); break;

                // Data - MongoDB
                case "mongo-crud": MongoDbExamples.RunBasicCrudExample(); break;
                case "mongo-bulk": MongoDbExamples.RunBulkOperationsExample(); break;
                case "mongo-config": MongoDbExamples.ShowConnectionConfiguration(); break;

                // Data - ElasticSearch
                case "es-crud": ElasticSearchExamples.RunBasicExample(); break;
                case "es-streaming": ElasticSearchExamples.RunStreamingExample(); break;
                case "es-health": ElasticSearchExamples.RunHealthCheckExample(); break;

                // Data - JSON
                case "json-crud": JsonStoreExamples.RunBasicExample(); break;
                case "json-multi": JsonStoreExamples.RunMultiStoreExample(); break;
                case "json-proto": JsonStoreExamples.RunPrototypingExample(); break;

                // Data - Sync
                case "sync-basic": await SyncExamples.RunBasicSyncExample(); break;
                case "sync-preview": await SyncExamples.RunPreviewSyncExample(); break;
                case "sync-multi-tenant": await SyncExamples.RunMultiTenantSyncExample(); break;
                case "sync-conflict": await SyncExamples.RunConflictResolutionExample(); break;
                case "sync-queue": await SyncExamples.RunSyncQueueExample(); break;
                case "sync-custom": await SyncExamples.RunCustomConflictResolverExample(); break;
                case "sync-tenant": await SyncExamples.RunTenantAwareSyncExample(); break;

                // Data - Tenant
                case "tenant-basic": await TenantExamples.RunBasicTenantContextExample(); break;
                case "tenant-scoped": await TenantExamples.RunScopedTenantExample(); break;
                case "tenant-store": await TenantExamples.RunTenantStoreWrapperExample(); break;
                case "tenant-async": await TenantExamples.RunAsyncScopedTenantExample(); break;
                case "tenant-non-tenant": await TenantExamples.RunNonTenantModeExample(); break;
                case "tenant-filter": await TenantExamples.RunFilteringExample(); break;
                case "tenant-security": await TenantExamples.RunSecurityExample(); break;

                // Communication - Network
                case "net-tcp": await NetworkExamples.RunTcpClientExample(); break;
                case "net-udp": await NetworkExamples.RunUdpExample(); break;
                case "net-info": NetworkExamples.ShowNetworkInformation(); break;

                // Communication - Hardware
                case "hw-serial": await HardwareExamples.RunSerialPortExample(); break;
                case "hw-lpt": await HardwareExamples.RunParallelPortExample(); break;
                case "hw-ir": await HardwareExamples.RunInfraredPortExample(); break;
                case "hw-list": HardwareExamples.ListAvailablePorts(); break;

                // Communication - Bluetooth
                case "bt-classic": await BluetoothExamples.RunClassicBluetoothExample(); break;
                case "bt-le": await BluetoothExamples.RunBluetoothLEExample(); break;
                case "bt-scan": await BluetoothExamples.ScanForDevices(); break;
                case "bt-info": BluetoothExamples.ShowAdapterInformation(); break;

                // Communication - WebSocket/SOAP/REST
                case "ws-server": await WebSocketExamples.RunWebSocketServerExample(); break;
                case "soap-server": await SoapUsageExamples.RunServerExample(); break;
                case "soap-client": await SoapUsageExamples.RunClientExample(); break;
                case "soap-cached": SoapUsageExamples.RunCachedClientExample(); break;
                case "rest-basic": await RestExamples.RunBasicExample(); break;

                // Communication - SSE
                case "sse-basic": await SseExamples.RunBasicServerExample(); break;
                case "sse-broadcast": await SseExamples.RunBroadcastExample(); break;
                case "sse-notify": await SseExamples.RunNotificationServerExample(); break;
                case "sse-client": await SseExamples.RunClientExample(); break;
                case "sse-ticker": await SseExamples.RunTypedEventsExample(); break;
                case "sse-auth": await SseExamples.RunAuthenticatedServerExample(); break;
                case "sse-middleware": await SseExamples.RunMiddlewareServerExample(); break;

                // Communication - Authentication
                case "auth-rest-log": await AuthenticationExamples.RestServerWithLogging(); break;
                case "auth-rest-ip": await AuthenticationExamples.RestServerWithIpBoundTokens(); break;
                case "auth-rest-client": await AuthenticationExamples.RestClientWithAuthentication(); break;
                case "auth-soap": await AuthenticationExamples.SoapServiceWithAuthentication(); break;

                // Models
                case "model-product": ProductModelsExamples.RunProductExample(); break;
                case "model-category": ProductModelsExamples.RunCategoryExample(); break;
                case "model-pricing": ProductModelsExamples.RunPricingExample(); break;
                case "model-inventory": ProductModelsExamples.RunInventoryExample(); break;

                // Validation
                case "val-basic": ValidationExamples.RunBasicValidationExample(); break;
                case "val-custom": ValidationExamples.RunCustomRuleExample(); break;
                case "val-async": await ValidationExamples.RunAsyncValidationExample(); break;

                // Caching
                case "cache-memory": await CachingExamples.RunMemoryCacheExample(); break;
                case "cache-expiration": await CachingExamples.RunCacheExpirationExample(); break;
                case "cache-redis": await CachingExamples.RunRedisCacheExample(); break;

                // Serialization
                case "ser-json": SerializationExamples.RunSystemJsonExample(); break;
                case "ser-xml": SerializationExamples.RunSystemXmlExample(); break;
                case "ser-newtonsoft": SerializationExamples.RunNewtonsoftExample(); break;
                case "ser-msgpack": SerializationExamples.RunMessagePackExample(); break;
                case "ser-protobuf": SerializationExamples.RunProtobufExample(); break;
                case "ser-compare": SerializationExamples.RunFormatComparisonExample(); break;

                // Security
                case "sec-password": SecurityExamples.RunPasswordHashingExample(); break;
                case "sec-encryption": SecurityExamples.RunEncryptionExample(); break;
                case "sec-jwt": SecurityExamples.RunJwtExample(); break;
                case "sec-aspnet-user": AspNetCoreSecurityExamples.RunCurrentUserExample(); break;
                case "sec-aspnet-perms": await AspNetCoreSecurityExamples.RunPermissionCheckerExample(); break;
                case "sec-aspnet-token": AspNetCoreSecurityExamples.RunTokenAdapterExample(); break;
                case "sec-aspnet-tenant": await AspNetCoreSecurityExamples.RunTenantResolverExample(); break;
                case "sec-aspnet-filter": await AspNetCoreSecurityExamples.RunEndpointFilterExample(); break;
                case "sec-aspnet-di": AspNetCoreSecurityExamples.RunDiRegistrationExample(); break;

                // Background Jobs
                case "bj-dispatcher": await BackgroundJobsExamples.RunDispatcherExample(); break;
                case "bj-processor": await BackgroundJobsExamples.RunProcessorExample(); break;
                case "bj-recurring": await BackgroundJobsExamples.RunRecurringSchedulerExample(); break;
                case "bj-retry": BackgroundJobsExamples.RunRetryPolicyExample(); break;
                case "bj-config": BackgroundJobsExamples.RunConfigurationExample(); break;

                // Message Queue
                case "mq-pubsub": await MessageQueueExamples.RunPubSubExample(); break;
                case "mq-typed": await MessageQueueExamples.RunTypedMessagesExample(); break;
                case "mq-ack": await MessageQueueExamples.RunManualAckExample(); break;
                case "mq-encrypt": MessageQueueExamples.RunEncryptionExample(); break;
                case "mq-fingerprint": MessageQueueExamples.RunFingerprintExample(); break;
                case "mq-mqtt-topics": MessageQueueExamples.RunMqttTopicsExample(); break;

                // Messaging
                case "msg-settings": MessagingExamples.RunEmailSettingsExample(); break;
                case "msg-email": MessagingExamples.RunEmailMessageExample(); break;
                case "msg-smtp": await MessagingExamples.RunSmtpSenderExample(); break;
                case "msg-template": await MessagingExamples.RunTemplateEngineExample(); break;
                case "msg-address": MessagingExamples.RunMessageAddressExample(); break;
                case "msg-result": MessagingExamples.RunMessageResultExample(); break;
                case "msg-sms-push": MessagingExamples.RunSmsAndPushExample(); break;
                case "msg-razor-inline": await RazorTemplateExamples.RunInlineRenderExample(); break;
                case "msg-razor-logic": await RazorTemplateExamples.RunConditionalAndLoopExample(); break;
                case "msg-razor-file": await RazorTemplateExamples.RunFileTemplateExample(); break;
                case "msg-razor-mt": await RazorTemplateExamples.RunMessageTemplateExample(); break;
                case "msg-razor-options": RazorTemplateExamples.RunOptionsExample(); break;
                case "msg-razor-errors": await RazorTemplateExamples.RunErrorHandlingExample(); break;
                case "msg-razor-compare": await RazorTemplateExamples.RunComparisonExample(); break;

                // Event Bus
                case "eb-inprocess": await EventBusExamples.RunInProcessExample(); break;
                case "eb-pipeline": await EventBusExamples.RunPipelineExample(); break;
                case "eb-dedup": await EventBusExamples.RunDeduplicationExample(); break;
                case "eb-topics": EventBusExamples.RunTopicConventionExample(); break;
                case "eb-distributed": await EventBusExamples.RunDistributedExample(); break;
                case "eb-outbox": await EventBusExamples.RunOutboxExample(); break;

                // Telemetry
                case "tel-store": TelemetryExamples.RunStoreInstrumentationExample(); break;
                case "tel-async": await TelemetryExamples.RunAsyncInstrumentationExample(); break;
                case "tel-tracing": TelemetryExamples.RunDistributedTracingExample(); break;
                case "tel-errors": TelemetryExamples.RunErrorTrackingExample(); break;
                case "tel-extensions": TelemetryExamples.RunExtensionMethodsExample(); break;
                case "tel-correlation": TelemetryExamples.RunCorrelationIdExample(); break;

                // Rules
                case "rules-basic": RulesExamples.RunBasicRulesExample(); break;
                case "rules-groups": RulesExamples.RunGroupsExample(); break;
                case "rules-object": RulesExamples.RunObjectContextExample(); break;
                case "rules-ruleset": RulesExamples.RunRuleSetExample(); break;
                case "rules-sql": RulesExamples.RunSqlConverterExample(); break;
                case "rules-spec": RulesExamples.RunSpecificationExample(); break;
                case "rules-validation": RulesExamples.RunValidationExample(); break;

                // Processors
                case "proc-csv-parser": ProcessorsExamples.RunCsvParserExample(); break;
                case "proc-csv": await ProcessorsExamples.RunCsvProcessorExample(); break;
                case "proc-csv-sync": ProcessorsExamples.RunCsvSyncExample(); break;
                case "proc-xml": await ProcessorsExamples.RunXmlProcessorExample(); break;
                case "proc-zip": await ProcessorsExamples.RunZipProcessorExample(); break;
                case "proc-composition": ProcessorsExamples.RunCompositionExample(); break;
                case "proc-errors": ProcessorsExamples.RunErrorHandlingExample(); break;

                // Storage
                case "stor-core": StorageExamples.RunCoreTypesExample(); break;
                case "stor-local": await StorageExamples.RunLocalStorageExample(); break;
                case "stor-tenant": await StorageExamples.RunTenantIsolationExample(); break;
                case "stor-errors": await StorageExamples.RunErrorHandlingExample(); break;
                case "stor-azure": StorageExamples.RunAzureBlobExample(); break;

                // Health
                case "hlth-core": HealthExamples.RunCoreTypesExample(); break;
                case "hlth-registration": HealthExamples.RunRegistrationExample(); break;
                case "hlth-runner": await HealthExamples.RunRunnerExample(); break;
                case "hlth-system": await HealthExamples.RunSystemChecksExample(); break;
                case "hlth-data": await HealthExamples.RunDataChecksExample(); break;
                case "hlth-redis": HealthExamples.RunRedisCheckExample(); break;
                case "hlth-fullstack": await HealthExamples.RunFullStackExample(); break;
                case "hlth-azure": HealthExamples.RunAzureChecksExample(); break;

                // About - Diagnostics
                case "health-check": await RunHealthCheck(); break;
                case "show-config": ShowConfiguration(); break;

                default:
                    Console.WriteLine($"Unknown example: {key}");
                    break;
            }
        }

        // ────────────────────────────────────────────────────────
        //  Diagnostics
        // ────────────────────────────────────────────────────────

        static async Task RunHealthCheck()
        {
            Examples.ExampleOutput.WriteLine("Health Check\n");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            void Check(string name, Action action)
            {
                try { action(); Examples.ExampleOutput.WriteSuccess($"PASS  {name}"); }
                catch (Exception ex) { Examples.ExampleOutput.WriteError($"FAIL  {name}: {ex.Message}"); }
            }

            Check("Configuration", () => _serviceProvider.GetRequiredService<FrameworkOptions>());
            Check("Logging", () => _serviceProvider.GetRequiredService<ILogger<Program>>());
            Check("DI Container", () => _serviceProvider.GetService<FrameworkOptions>());
            Check("Config Sections", () =>
            {
                _serviceProvider.GetRequiredService<DataOptions>();
                _serviceProvider.GetRequiredService<CommunicationOptions>();
            });

            await Task.Delay(1);
            Examples.ExampleOutput.WriteSuccess("PASS  Async Support");

            sw.Stop();
            Examples.ExampleOutput.WriteLine($"\nCompleted in {sw.ElapsedMilliseconds}ms");
        }

        static void ShowConfiguration()
        {
            Examples.ExampleOutput.WriteLine("Configuration\n");

            try
            {
                var fw = _serviceProvider.GetRequiredService<FrameworkOptions>();
                var data = _serviceProvider.GetRequiredService<DataOptions>();
                var comm = _serviceProvider.GetRequiredService<CommunicationOptions>();

                Examples.ExampleOutput.WriteInfo("Framework", $"{fw.Name} v{fw.Version} ({fw.Environment})");
                Examples.ExampleOutput.WriteInfo("Store", $"{data.DefaultStoreType}");
                Examples.ExampleOutput.WriteInfo("WebSocket", $"{(comm.WebSocket.Enabled ? "Enabled" : "Disabled")} (:{comm.WebSocket.Port})");
                Examples.ExampleOutput.WriteInfo("SSE", $"{(comm.SSE.Enabled ? "Enabled" : "Disabled")} (:{comm.SSE.Port})");
                Examples.ExampleOutput.WriteInfo(".NET", $"{Environment.Version}");
                Examples.ExampleOutput.WriteInfo("OS", $"{Environment.OSVersion}");
                Examples.ExampleOutput.WriteInfo("Machine", $"{Environment.MachineName}");
            }
            catch (Exception ex)
            {
                Examples.ExampleOutput.WriteError($"Error: {ex.Message}");
            }
        }
    }

}
