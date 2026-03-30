using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Servers;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.WebSocket
    /// </summary>
    public static class WebSocketExamples
    {
        /// <summary>
        /// Example: Echo server that broadcasts received messages back to all clients
        /// </summary>
        public static async Task RunWebSocketServerExample()
        {
            ExampleOutput.WriteLine("=== WebSocket Echo Server ===\n");

            using var server = new WebSocketServer();

            server.OnClientConnected += (sender, clientId) =>
            {
                ExampleOutput.WriteLine($"Client connected: {clientId}");
            };

            server.OnClientDisconnected += (sender, clientId) =>
            {
                ExampleOutput.WriteLine($"Client disconnected: {clientId}");
            };

            server.OnDataReceived += (sender, data) =>
            {
                var message = Encoding.UTF8.GetString(data);
                ExampleOutput.WriteLine($"Received: {message}");

                // Echo back to all clients
                var echo = Encoding.UTF8.GetBytes($"Echo: {message}");
                server.BroadcastAsync(echo).GetAwaiter().GetResult();
            };

            try
            {
                var cts = new CancellationTokenSource();
                _ = Task.Run(() => server.StartAsync("http://localhost:5000/ws/", cts.Token));

                ExampleOutput.WriteLine("Server running on ws://localhost:5000/ws/");
                ExampleOutput.WriteLine("Press ENTER to stop...");
                ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

                await server.StopAsync();
                ExampleOutput.WriteLine("Server stopped");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"WebSocket Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Periodic broadcast to all connected clients
        /// </summary>
        public static async Task RunBroadcastExample()
        {
            ExampleOutput.WriteLine("=== WebSocket Broadcast Server ===\n");

            using var server = new WebSocketServer();
            var messageCount = 0;

            server.OnClientConnected += (sender, clientId) =>
            {
                ExampleOutput.WriteLine($"Client joined: {clientId[..8]}");
            };

            var cts = new CancellationTokenSource();
            _ = Task.Run(() => server.StartAsync("http://localhost:5001/ws/", cts.Token));

            ExampleOutput.WriteLine("Broadcast server on ws://localhost:5001/ws/");
            ExampleOutput.WriteLine("Broadcasting every 5 seconds. Press ENTER to stop...\n");

            var broadcastTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(5000, cts.Token);
                    messageCount++;

                    var payload = Encoding.UTF8.GetBytes(
                        $"Broadcast #{messageCount} at {DateTime.Now:HH:mm:ss}");
                    await server.BroadcastAsync(payload);
                    ExampleOutput.WriteLine($"Sent broadcast #{messageCount}");
                }
            });

            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();

            try { await broadcastTask; }
            catch (OperationCanceledException) { }

            ExampleOutput.WriteLine("Server stopped");
        }

        /// <summary>
        /// Example: Simple chat room server
        /// </summary>
        public static async Task RunChatServerExample()
        {
            ExampleOutput.WriteLine("=== WebSocket Chat Server ===\n");

            using var server = new WebSocketServer();

            server.OnClientConnected += (sender, clientId) =>
            {
                var joinMsg = Encoding.UTF8.GetBytes($"[System] User {clientId[..8]} joined");
                server.BroadcastAsync(joinMsg).GetAwaiter().GetResult();
                ExampleOutput.WriteLine($"User {clientId[..8]} joined");
            };

            server.OnClientDisconnected += (sender, clientId) =>
            {
                var leaveMsg = Encoding.UTF8.GetBytes($"[System] User {clientId[..8]} left");
                server.BroadcastAsync(leaveMsg).GetAwaiter().GetResult();
                ExampleOutput.WriteLine($"User {clientId[..8]} left");
            };

            server.OnDataReceived += (sender, data) =>
            {
                var text = Encoding.UTF8.GetString(data);
                var chatMsg = Encoding.UTF8.GetBytes($"[{DateTime.Now:HH:mm}] {text}");
                server.BroadcastAsync(chatMsg).GetAwaiter().GetResult();
                ExampleOutput.WriteLine($"Chat: {text}");
            };

            var cts = new CancellationTokenSource();
            _ = Task.Run(() => server.StartAsync("http://localhost:5002/chat/", cts.Token));

            ExampleOutput.WriteLine("Chat server on ws://localhost:5002/chat/");
            ExampleOutput.WriteLine("Press ENTER to stop...");
            ExampleOutput.WriteLine("(Demo: would wait for input in console mode)");

            cts.Cancel();
            await server.StopAsync();
            ExampleOutput.WriteLine("Chat server stopped");
        }
    }
}
