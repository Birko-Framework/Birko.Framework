using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Bluetooth.Ports;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.Bluetooth
    /// </summary>
    public static class BluetoothExamples
    {
        /// <summary>
        /// Example: Classic Bluetooth communication via virtual COM port (RFCOMM/SPP)
        /// </summary>
        public static async Task RunClassicBluetoothExample()
        {
            ExampleOutput.WriteLine("=== Classic Bluetooth Example ===\n");

            // BluetoothSettings extends SerialSettings (classic BT uses virtual COM port)
            var settings = new BluetoothSettings
            {
                Name = "COM6",
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One
            };

            var bluetooth = new Bluetooth(settings);

            bluetooth.SubscribeProcessData(() =>
            {
                if (bluetooth.HasReadData(0))
                {
                    var data = bluetooth.Read(-1);
                    ExampleOutput.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
                    bluetooth.RemoveReadData(data.Length);
                }
            });

            try
            {
                bluetooth.Open();
                ExampleOutput.WriteLine($"Connected via {settings.Name} at {settings.BaudRate} baud");

                var message = "Hello via Bluetooth!";
                bluetooth.Write(Encoding.UTF8.GetBytes(message));
                ExampleOutput.WriteLine($"Sent: {message}");

                await Task.Delay(1000);

                var response = bluetooth.Read(-1);
                if (response.Length > 0)
                {
                    ExampleOutput.WriteLine($"Response: {Encoding.UTF8.GetString(response)}");
                    bluetooth.RemoveReadData(response.Length);
                }

                bluetooth.Close();
                ExampleOutput.WriteLine("Connection closed");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Bluetooth Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Bluetooth Low Energy (BLE) communication
        /// </summary>
        public static async Task RunBluetoothLEExample()
        {
            ExampleOutput.WriteLine("=== Bluetooth Low Energy Example ===\n");

            var batteryServiceUuid = Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB");
            var batteryLevelUuid = Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB");

            var settings = new BluetoothLESettings
            {
                Name = "MyBLESensor",
                DeviceAddress = "AA:BB:CC:DD:EE:FF",
                ServiceUuid = batteryServiceUuid,
                CharacteristicUuid = batteryLevelUuid,
                ConnectionTimeout = 10000,
                AutoReconnect = true,
                MaxReconnectAttempts = 3
            };

            var ble = new BluetoothLE(settings);

            ble.SubscribeProcessData(() =>
            {
                if (ble.HasReadData(0))
                {
                    var data = ble.Read(-1);
                    ExampleOutput.WriteLine($"BLE notification: {BitConverter.ToString(data)}");
                    ble.RemoveReadData(data.Length);
                }
            });

            try
            {
                ble.Open();
                ExampleOutput.WriteLine($"Connected to {settings.DeviceAddress}");

                // Write a command
                ble.Write(new byte[] { 0x01 });
                ExampleOutput.WriteLine("Wrote command to characteristic");

                // Read response
                await Task.Delay(1000);
                var response = ble.Read(-1);
                if (response.Length > 0)
                {
                    ExampleOutput.WriteLine($"Read: {BitConverter.ToString(response)}");
                    ble.RemoveReadData(response.Length);
                }

                // Wait for notifications
                ExampleOutput.WriteLine("Listening for notifications (3 seconds)...");
                await Task.Delay(3000);

                ble.Close();
                ExampleOutput.WriteLine("Disconnected");
            }
            catch (PlatformNotSupportedException)
            {
                ExampleOutput.WriteLine("BLE is not supported on this platform.");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"BLE Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Scan for nearby BLE devices
        /// </summary>
        public static async Task ScanForDevices()
        {
            ExampleOutput.WriteLine("=== BLE Device Scanner ===\n");

            try
            {
                ExampleOutput.WriteLine("Scanning for BLE devices (10 seconds)...");

                using var cts = new CancellationTokenSource();
                var devices = await BluetoothLEDevices.DiscoverDevicesAsync(
                    timeout: TimeSpan.FromSeconds(10),
                    cancellationToken: cts.Token);

                ExampleOutput.WriteLine($"\nFound {devices.Count} device(s):\n");

                foreach (var device in devices)
                {
                    ExampleOutput.WriteLine($"  {device.Name ?? "Unknown"}");
                    ExampleOutput.WriteLine($"    Address: {device.Address}");
                    ExampleOutput.WriteLine($"    RSSI:    {device.Rssi} dBm");

                    if (device.ServiceUuids.Count > 0)
                    {
                        ExampleOutput.WriteLine($"    Services: {string.Join(", ", device.ServiceUuids)}");
                    }
                    ExampleOutput.WriteLine();
                }
            }
            catch (PlatformNotSupportedException)
            {
                ExampleOutput.WriteLine("BLE scanning is not supported on this platform.");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Scan Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Display Bluetooth adapter information
        /// </summary>
        public static void ShowAdapterInformation()
        {
            ExampleOutput.WriteLine("=== Bluetooth Adapter Info ===\n");
            ExampleOutput.WriteLine($"  Platform: {Environment.OSVersion.Platform}");
            ExampleOutput.WriteLine($"  OS:       {Environment.OSVersion.VersionString}");
            ExampleOutput.WriteLine("  Classic BT uses virtual COM ports (Serial)");
            ExampleOutput.WriteLine("  BLE requires Windows 10+ or Linux with BlueZ");
        }
    }
}
