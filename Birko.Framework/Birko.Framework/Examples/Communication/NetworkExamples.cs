using System;
using System.Text;
using System.Threading.Tasks;
using Birko.Communication.Network.Ports;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.Network
    /// </summary>
    public static class NetworkExamples
    {
        /// <summary>
        /// Example: TCP/IP client communication
        /// </summary>
        public static async Task RunTcpClientExample()
        {
            ExampleOutput.WriteLine("=== TCP/IP Client Example ===\n");

            var settings = new TcpIpSettings
            {
                Name = "ExampleTcpClient",
                Address = "127.0.0.1",
                Port = 8080
            };

            var tcp = new TcpIp(settings);

            // Subscribe to incoming data via the AbstractPort delegate
            tcp.SubscribeProcessData(() =>
            {
                if (tcp.HasReadData(0))
                {
                    var data = tcp.Read(-1);
                    var message = Encoding.UTF8.GetString(data);
                    ExampleOutput.WriteLine($"Received: {message}");
                    tcp.RemoveReadData(data.Length);
                }
            });

            try
            {
                tcp.Open();
                ExampleOutput.WriteLine($"Connected to {settings.Address}:{settings.Port}");

                var message = "Hello, TCP Server!";
                tcp.Write(Encoding.UTF8.GetBytes(message));
                ExampleOutput.WriteLine($"Sent: {message}");

                // Allow time for response
                await Task.Delay(1000);

                // Manual read check
                if (tcp.HasReadData(0))
                {
                    var response = tcp.Read(-1);
                    ExampleOutput.WriteLine($"Response: {Encoding.UTF8.GetString(response)}");
                    tcp.RemoveReadData(response.Length);
                }

                tcp.Close();
                ExampleOutput.WriteLine("Connection closed");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"TCP Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: UDP connectionless communication
        /// </summary>
        public static async Task RunUdpExample()
        {
            ExampleOutput.WriteLine("=== UDP Communication Example ===\n");

            var settings = new UdpSettings
            {
                Name = "ExampleUdp",
                Address = "127.0.0.1",
                Port = 9090,
                LocalPort = 9091
            };

            var udp = new Udp(settings);

            udp.SubscribeProcessData(() =>
            {
                if (udp.HasReadData(0))
                {
                    var data = udp.Read(-1);
                    ExampleOutput.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
                    udp.RemoveReadData(data.Length);
                }
            });

            try
            {
                udp.Open();
                ExampleOutput.WriteLine($"UDP socket open on {settings.Address}:{settings.Port}");

                var data = Encoding.UTF8.GetBytes("Hello via UDP!");
                udp.Write(data);
                ExampleOutput.WriteLine("Sent: Hello via UDP!");

                await Task.Delay(1000);

                udp.Close();
                ExampleOutput.WriteLine("UDP socket closed");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"UDP Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Display basic network information
        /// </summary>
        public static void ShowNetworkInformation()
        {
            ExampleOutput.WriteLine("=== Network Information ===\n");
            ExampleOutput.WriteLine($"  Machine Name:  {Environment.MachineName}");
            ExampleOutput.WriteLine($"  OS Version:    {Environment.OSVersion}");
            ExampleOutput.WriteLine($"  .NET Version:  {Environment.Version}");
        }
    }
}
