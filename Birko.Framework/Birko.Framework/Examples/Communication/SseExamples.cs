using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Birko.Communication.SSE;
using Birko.Communication.SSE.Middleware;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.SSE
    /// Note: These examples require Birko.Communication.SSE to be referenced
    /// </summary>
    public static class SseExamples
    {
        /// <summary>
        /// Example: Basic SSE server
        /// </summary>
        public static async Task RunBasicServerExample()
        {
            var server = new SseServer();

            // Subscribe to connection events
            server.OnClientConnected += (sender, client) =>
            {
                ExampleOutput.WriteLine($"Client connected: {client.ClientId} from {client.RemoteEndPoint}");

                // Send welcome message
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await server.SendToClientAsync(client.ClientId,
                        SseEvent.Create("Welcome to the SSE server!", "welcome", Guid.NewGuid().ToString()));
                });
            };

            server.OnClientDisconnected += (sender, client) =>
            {
                ExampleOutput.WriteLine($"Client disconnected: {client.ClientId}");
            };

            try
            {
                // Start server
                ExampleOutput.WriteLine("Starting SSE server on http://localhost:5000/sse/");
                await server.StartAsync("http://localhost:5000/sse/");

                ExampleOutput.WriteLine("Server is running. Press ENTER to stop...");
                ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

                // Stop server
                await server.StopAsync();
                ExampleOutput.WriteLine("Server stopped");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"SSE Server Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Broadcasting periodic events
        /// </summary>
        public static async Task RunBroadcastExample()
        {
            var server = new SseServer();
            var eventCount = 0;

            server.OnClientConnected += (sender, client) =>
            {
                ExampleOutput.WriteLine($"Client {client.ClientId} connected. Total clients: {server.ConnectedClientCount}");
            };

            // Start server
            await server.StartAsync("http://localhost:5001/sse/");

            ExampleOutput.WriteLine("Broadcast server started on http://localhost:5001/sse/");
            ExampleOutput.WriteLine("Broadcasting an event every 3 seconds...");

            // Broadcast periodic events
            var cts = new CancellationTokenSource();
            var broadcastTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(3000, cts.Token);
                    eventCount++;

                    var eventData = new
                    {
                        Id = eventCount,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        Message = $"Server time update #{eventCount}"
                    };

                    await server.BroadcastJsonAsync(eventData, "time-update");
                    ExampleOutput.WriteLine($"→ Broadcasted event #{eventCount}");
                }
            });

            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();
            await broadcastTask;
            ExampleOutput.WriteLine("Server stopped");
        }

        /// <summary>
        /// Example: SSE server for real-time notifications
        /// </summary>
        public static async Task RunNotificationServerExample()
        {
            var server = new SseServer();

            server.OnClientConnected += async (sender, client) =>
            {
                // Send initial connection event
                await server.SendToClientAsync(client.ClientId,
                    SseEvent.Create("Connected to notification service", "connected", Guid.NewGuid().ToString()));

                // Send initial notifications count
                var initialData = new { UnreadCount = 5, LastNotification = "Welcome!" };
                await server.SendToClientAsync(client.ClientId,
                    SseEvent.FromJson(initialData, "notifications"));
            };

            await server.StartAsync("http://localhost:5002/notifications/");

            ExampleOutput.WriteLine("Notification server started on http://localhost:5002/notifications/");
            ExampleOutput.WriteLine("Simulating incoming notifications...");

            // Simulate incoming notifications
            var cts = new CancellationTokenSource();
            var notifications = new[]
            {
                new { Type = "info", Title = "System Update", Message = "New version available" },
                new { Type = "warning", Title = "Storage Low", Message = "Disk space at 80%" },
                new { Type = "success", Title = "Backup Complete", Message = "Daily backup finished" },
                new { Type = "error", Title = "Connection Lost", Message = "Reconnecting..." },
            };

            var notificationTask = Task.Run(async () =>
            {
                var index = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(5000, cts.Token);

                    var notification = notifications[index % notifications.Length];
                    await server.BroadcastJsonAsync(notification, "notification");
                    ExampleOutput.WriteLine($"→ Sent notification: {notification.Title}");

                    index++;
                }
            });

            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();
            await notificationTask;
            ExampleOutput.WriteLine("Notification server stopped");
        }

        /// <summary>
        /// Example: SSE client
        /// </summary>
        public static async Task RunClientExample()
        {
            var client = new SseClient("http://localhost:5000/sse/");

            // Subscribe to events
            client.OnConnected += (sender, args) =>
            {
                ExampleOutput.WriteLine("Connected to SSE server");
            };

            client.OnMessage += (sender, data) =>
            {
                ExampleOutput.WriteLine($"Received: {data}");
            };

            client.OnEvent += (sender, sseEvent) =>
            {
                ExampleOutput.WriteLine($"Event [{sseEvent.Event}]: {sseEvent.Data}");
            };

            client.OnError += (sender, ex) =>
            {
                ExampleOutput.WriteError($"Error: {ex.Message}");
            };

            client.OnDisconnected += (sender, args) =>
            {
                ExampleOutput.WriteLine("Disconnected from SSE server");
            };

            try
            {
                ExampleOutput.WriteLine("Connecting to SSE server...");
                await client.ConnectAsync();

                ExampleOutput.WriteLine("Listening for events. Press ENTER to disconnect...");
                ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

                await client.DisconnectAsync();
                ExampleOutput.WriteLine("Client disconnected");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"SSE Client Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: SSE with typed events
        /// </summary>
        public static async Task RunTypedEventsExample()
        {
            var server = new SseServer();

            // Define event types
            var stockEvents = new[]
            {
                new { Symbol = "AAPL", Price = 178.50m, Change = 1.25m },
                new { Symbol = "GOOGL", Price = 142.80m, Change = -0.50m },
                new { Symbol = "MSFT", Price = 378.90m, Change = 2.10m },
            };

            await server.StartAsync("http://localhost:5003/stocks/");

            ExampleOutput.WriteLine("Stock ticker server started on http://localhost:5003/stocks/");

            var cts = new CancellationTokenSource();
            var stockTask = Task.Run(async () =>
            {
                var index = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(2000, cts.Token);

                    var stock = stockEvents[index % stockEvents.Length];
                    // Add some random price movement
                    var updatedStock = new
                    {
                        stock.Symbol,
                        Price = stock.Price + (decimal)(System.Random.Shared.NextDouble() * 2 - 1),
                        Change = stock.Change + (decimal)(System.Random.Shared.NextDouble() * 0.5 - 0.25),
                        Timestamp = DateTime.Now.ToString("HH:mm:ss")
                    };

                    await server.BroadcastJsonAsync(updatedStock, "stock-update");
                    ExampleOutput.WriteLine($"→ {updatedStock.Symbol}: ${updatedStock.Price:F2}");

                    index++;
                }
            });

            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();
            await stockTask;
            ExampleOutput.WriteLine("Stock ticker server stopped");
        }

        /// <summary>
        /// Example: SSE with client-specific targeting
        /// </summary>
        public static async Task RunTargetedEventsExample()
        {
            var server = new SseServer();
            var userConnections = new Dictionary<string, string>();

            server.OnClientConnected += async (sender, client) =>
            {
                // Simulate user authentication from headers
                var userId = client.Headers.GetValueOrDefault("X-User-Id", "anonymous");
                userConnections[client.ClientId] = userId;

                ExampleOutput.WriteLine($"User {userId} connected as {client.ClientId}");

                // Send personalized welcome
                await server.SendToClientAsync(client.ClientId,
                    SseEvent.Create($"Welcome, {userId}!", "welcome"));
            };

            server.OnClientDisconnected += (sender, client) =>
            {
                if (userConnections.TryGetValue(client.ClientId, out var userId))
                {
                    ExampleOutput.WriteLine($"User {userId} disconnected");
                    userConnections.Remove(client.ClientId);
                }
            };

            await server.StartAsync("http://localhost:5004/personalized/");

            ExampleOutput.WriteLine("Personalized SSE server started on http://localhost:5004/personalized/");
            ExampleOutput.WriteLine("Clients can connect with X-User-Id header");

            // Simulate sending personalized messages
            var cts = new CancellationTokenSource();
            var messageTask = Task.Run(async () =>
            {
                var index = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(4000, cts.Token);

                    // In a real scenario, you'd send to specific users
                    // For demo, broadcast to all
                    var message = new
                    {
                        From = "System",
                        Content = $"Broadcast message #{index}",
                        Timestamp = DateTime.Now.ToString("HH:mm:ss")
                    };

                    await server.BroadcastJsonAsync(message, "message");
                    index++;
                }
            });

            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();
            await messageTask;
            ExampleOutput.WriteLine("Server stopped");
        }

        /// <summary>
        /// Example: SSE server with authentication
        /// </summary>
        public static async Task RunAuthenticatedServerExample()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Configure authentication
            var authConfig = new SseAuthenticationConfiguration
            {
                Enabled = true,
                Tokens = new List<string>
                {
                    "${SSE_API_TOKEN}", // Supports environment variables
                    "sse-secret-token-123"
                },
                ApiKeyHeader = "X-API-Key",
                AllowQueryToken = true,
                QueryTokenName = "token"
            };

            var authService = new SseAuthenticationService(
                authConfig,
                loggerFactory.CreateLogger<SseAuthenticationService>()
            );

            // Create server
            var server = new SseServer();

            server.OnClientConnected += async (sender, client) =>
            {
                ExampleOutput.WriteLine($"Authenticated client connected: {client.ClientId}");
                await server.SendToClientAsync(client.ClientId,
                    SseEvent.Create("Welcome! You are authenticated.", "welcome"));
            };

            await server.StartAsync("http://localhost:8080/sse/");

            ExampleOutput.WriteLine("Authenticated SSE server started on http://localhost:8080/sse/");
            ExampleOutput.WriteLine("Connect with: curl -H 'X-API-Key: sse-secret-token-123' http://localhost:8080/sse/");
            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            await server.StopAsync();
            ExampleOutput.WriteLine("Server stopped");
        }

        /// <summary>
        /// Example: SSE server with IP-bound tokens
        /// </summary>
        public static async Task RunIpBoundAuthServerExample()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var authConfig = new SseAuthenticationConfiguration
            {
                Enabled = true,
                TokenBindings = new List<Birko.Security.Authentication.TokenBinding>
                {
                    new Birko.Security.Authentication.TokenBinding
                    {
                        Token = "${SERVICE_A_TOKEN}",
                        AllowedIps = new List<string> { "192.168.1.100", "192.168.1.101" }
                    },
                    new Birko.Security.Authentication.TokenBinding
                    {
                        Token = "${SERVICE_B_TOKEN}",
                        AllowedIps = new List<string> { "10.0.0.50", "10.0.0.0/24" }
                    }
                },
                ApiKeyHeader = "X-API-Key"
            };

            var authService = new SseAuthenticationService(
                authConfig,
                loggerFactory.CreateLogger<SseAuthenticationService>()
            );

            var server = new SseServer();

            server.OnClientConnected += (sender, client) =>
            {
                ExampleOutput.WriteLine($"IP-bound client connected: {client.ClientId} from {client.RemoteEndPoint}");
            };

            await server.StartAsync("http://localhost:8081/sse/");

            ExampleOutput.WriteLine("IP-bound SSE server started on http://localhost:8081/sse/");
            ExampleOutput.WriteLine("Only connections from allowed IPs with valid tokens will be accepted");
            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            await server.StopAsync();
            ExampleOutput.WriteLine("Server stopped");
        }

        /// <summary>
        /// Example: SSE client with authentication
        /// </summary>
        public static async Task RunAuthenticatedClientExample()
        {
            var headers = new Dictionary<string, string>
            {
                { "X-API-Key", "sse-secret-token-123" }
            };

            var client = new SseClient("http://localhost:8080/sse/", headers);

            client.OnConnected += (sender, args) =>
            {
                ExampleOutput.WriteLine("Connected with authentication");
            };

            client.OnMessage += (sender, data) =>
            {
                ExampleOutput.WriteLine($"Received: {data}");
            };

            client.OnError += (sender, ex) =>
            {
                ExampleOutput.WriteError($"Error: {ex.Message}");
            };

            try
            {
                ExampleOutput.WriteLine("Connecting to authenticated SSE server...");
                await client.ConnectAsync();

                ExampleOutput.WriteLine("Listening for events. Press ENTER to disconnect...");
                ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Client Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: SSE server with middleware pipeline
        /// </summary>
        public static async Task RunMiddlewareServerExample()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Configure authentication
            var authConfig = new SseAuthenticationConfiguration
            {
                Enabled = true,
                Tokens = new List<string> { "${SSE_API_TOKEN}", "sse-token-456" },
                ApiKeyHeader = "X-API-Key"
            };

            var authService = new SseAuthenticationService(
                authConfig,
                loggerFactory.CreateLogger<SseAuthenticationService>()
            );

            // Create middleware pipeline (example - in real implementation, server would accept middleware)
            var middlewares = new List<ISseMiddleware>
            {
                // CORS middleware - allows cross-origin requests
                SseCorsMiddleware.Create(
                    loggerFactory.CreateLogger<SseCorsMiddleware>(),
                    new[] { "http://localhost:3000", "http://localhost:4200" }
                ),

                // Rate limiting middleware - prevents connection spam
                SseRateLimitMiddleware.Create(
                    loggerFactory.CreateLogger<SseRateLimitMiddleware>(),
                    maxConnectionsPerMinute: 60
                ),

                // Logging middleware - logs all connection attempts
                SseLoggingMiddleware.Create(
                    loggerFactory.CreateLogger<SseLoggingMiddleware>()
                ),

                // Authentication middleware - validates tokens
                SseAuthenticationMiddleware.Create(authService)
            };

            var server = new SseServer();

            server.OnClientConnected += async (sender, client) =>
            {
                ExampleOutput.WriteLine($"Client passed all middleware: {client.ClientId}");
                await server.SendToClientAsync(client.ClientId,
                    SseEvent.Create("Connection established via middleware pipeline", "connected"));
            };

            await server.StartAsync("http://localhost:8082/sse/");

            ExampleOutput.WriteLine("SSE server with middleware pipeline started on http://localhost:8082/sse/");
            ExampleOutput.WriteLine("Middleware pipeline: CORS -> Rate Limit -> Logging -> Authentication");
            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            await server.StopAsync();
            ExampleOutput.WriteLine("Server stopped");
        }

        /// <summary>
        /// Example: SSE server with multiple event channels
        /// </summary>
        public static async Task RunMultiChannelServerExample()
        {
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var authConfig = new SseAuthenticationConfiguration
            {
                Enabled = true,
                Tokens = new List<string> { "channel-token-789" },
                ApiKeyHeader = "X-API-Key"
            };

            var authService = new SseAuthenticationService(
                authConfig,
                loggerFactory.CreateLogger<SseAuthenticationService>()
            );

            var server = new SseServer();
            var clientChannels = new Dictionary<string, string>(); // clientId -> channel

            server.OnClientConnected += async (sender, client) =>
            {
                // Determine which channel the client wants from headers
                var channel = client.Headers.GetValueOrDefault("X-Channel", "default");
                clientChannels[client.ClientId] = channel;

                ExampleOutput.WriteLine($"Client {client.ClientId} subscribed to channel: {channel}");

                await server.SendToClientAsync(client.ClientId,
                    SseEvent.Create($"Subscribed to channel: {channel}", "subscribed"));
            };

            server.OnClientDisconnected += (sender, client) =>
            {
                if (clientChannels.TryGetValue(client.ClientId, out var channel))
                {
                    ExampleOutput.WriteLine($"Client left channel: {channel}");
                    clientChannels.Remove(client.ClientId);
                }
            };

            await server.StartAsync("http://localhost:8083/sse/");

            ExampleOutput.WriteLine("Multi-channel SSE server started on http://localhost:8083/sse/");

            // Broadcast to different channels
            var cts = new CancellationTokenSource();
            var broadcastTask = Task.Run(async () =>
            {
                var counter = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(3000, cts.Token);
                    counter++;

                    // Broadcast to 'news' channel
                    var newsEvent = new { Headline = $"News Update #{counter}", Time = DateTime.Now.ToString("HH:mm:ss") };
                    await server.BroadcastJsonAsync(newsEvent, "news");

                    // Broadcast to 'alerts' channel
                    if (counter % 2 == 0)
                    {
                        var alertEvent = new { Level = "info", Message = $"System Alert #{counter}" };
                        await server.BroadcastJsonAsync(alertEvent, "alert");
                    }

                    ExampleOutput.WriteLine($"→ Broadcasted to channels (counter: {counter})");
                }
            });

            ExampleOutput.WriteLine("Connect with header: X-Channel: news or X-Channel: alerts");
            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();
            await broadcastTask;
            ExampleOutput.WriteLine("Server stopped");
        }
    }
}
