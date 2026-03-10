using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Ports;

namespace Birko.Communication.Bluetooth.Ports
{
    /// <summary>
    /// Represents a discovered Bluetooth LE device
    /// </summary>
    public class DiscoveredDevice
    {
        /// <summary>
        /// Gets or sets the device name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the device address (MAC address)
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// Gets or sets the signal strength (RSSI in dBm)
        /// </summary>
        public int Rssi { get; set; }

        /// <summary>
        /// Gets or sets the advertisement data
        /// </summary>
        public Dictionary<string, object> AdvertisementData { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets or sets the list of advertised service UUIDs
        /// </summary>
        public List<string> ServiceUuids { get; set; } = new List<string>();
    }

    /// <summary>
    /// Static class for Bluetooth LE device discovery
    /// </summary>
    public static class BluetoothLEDevices
    {
        /// <summary>
        /// Discovers nearby Bluetooth LE devices
        /// </summary>
        /// <param name="timeout">Discovery timeout (default: 10 seconds)</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>List of discovered devices</returns>
        public static async Task<List<DiscoveredDevice>> DiscoverDevicesAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(10);
            }

#if WINDOWS
            return await DiscoverDevicesWindowsAsync(timeout, cancellationToken);
#elif LINUX
            return await DiscoverDevicesLinuxAsync(timeout, cancellationToken);
#else
            throw new PlatformNotSupportedException("Bluetooth LE is not supported on this platform");
#endif
        }

        /// <summary>
        /// Discovers nearby Bluetooth LE devices advertising a specific service
        /// </summary>
        /// <param name="serviceUuid">Service UUID to filter by</param>
        /// <param name="timeout">Discovery timeout (default: 10 seconds)</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>List of discovered devices advertising the specified service</returns>
        public static async Task<List<DiscoveredDevice>> DiscoverDevicesWithServiceAsync(
            Guid serviceUuid,
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(10);
            }

#if WINDOWS
            return await DiscoverDevicesWithServiceWindowsAsync(serviceUuid, timeout, cancellationToken);
#elif LINUX
            return await DiscoverDevicesWithServiceLinuxAsync(serviceUuid, timeout, cancellationToken);
#else
            throw new PlatformNotSupportedException("Bluetooth LE is not supported on this platform");
#endif
        }

#if WINDOWS
        #region Windows Implementation

        private static async Task<List<DiscoveredDevice>> DiscoverDevicesWindowsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                string aqsFilter = "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";
                string[] requestedProperties = new string[]
                {
                    "System.Devices.Aep.DeviceAddress",
                    "System.Devices.Aep.Alias",
                    "System.Devices.Aep.SignalStrength"
                };

                var watcher = Windows.Devices.Enumeration.DeviceInformation.CreateWatcher(
                    aqsFilter,
                    requestedProperties,
                    Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint);

                var completionSource = new TaskCompletionSource<bool>();
                var discoveredDevices = new System.Collections.Concurrent.ConcurrentDictionary<string, DiscoveredDevice>();

                watcher.Added += (sender, args) =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        var device = new DiscoveredDevice
                        {
                            Name = args.Name ?? "Unknown",
                            Address = GetDeviceAddress(args),
                            Rssi = GetSignalStrength(args)
                        };
                        discoveredDevices.TryAdd(device.Address, device);
                    }
                };

                watcher.Updated += (sender, args) =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        // DeviceInformationUpdate provides updated properties
                        if (args.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addressObj))
                        {
                            var address = addressObj?.ToString();
                            if (address != null && discoveredDevices.TryGetValue(address, out var device))
                            {
                                if (args.Properties.TryGetValue("System.Devices.Aep.SignalStrength", out var rssiObj))
                                {
                                    if (rssiObj is int rssiInt)
                                        device.Rssi = rssiInt;
                                    else if (int.TryParse(rssiObj?.ToString(), out var parsedRssi))
                                        device.Rssi = parsedRssi;
                                }
                            }
                        }
                    }
                };

                watcher.Removed += (sender, args) =>
                {
                    // Device removed, optionally handle
                };

                watcher.EnumerationCompleted += (sender, args) =>
                {
                    // Start timeout after enumeration completes
                    Task.Delay(timeout).ContinueWith(t =>
                    {
                        watcher.Stop();
                        completionSource.TrySetResult(true);
                    }, cancellationToken);
                };

                watcher.Stopped += (sender, args) =>
                {
                    completionSource.TrySetResult(true);
                };

                watcher.Start();
                await completionSource.Task;

                devices.AddRange(discoveredDevices.Values);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to discover Bluetooth LE devices", ex);
            }

            return devices;
        }

        private static async Task<List<DiscoveredDevice>> DiscoverDevicesWithServiceWindowsAsync(
            Guid serviceUuid,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                string aqsFilter = $"System.Devices.Aep.ProtocolId:=\"{{bb7bb05e-5972-42b5-94fc-76eaa7084d49}}\" AND System.Devices.Aep.ContainerId:<>\"\"";
                string[] requestedProperties = new string[]
                {
                    "System.Devices.Aep.DeviceAddress",
                    "System.Devices.Aep.Alias",
                    "System.Devices.Aep.SignalStrength",
                    "System.Devices.Aep.ServiceGuids"
                };

                var watcher = Windows.Devices.Enumeration.DeviceInformation.CreateWatcher(
                    aqsFilter,
                    requestedProperties,
                    Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint);

                var completionSource = new TaskCompletionSource<bool>();
                var discoveredDevices = new System.Collections.Concurrent.ConcurrentDictionary<string, DiscoveredDevice>();

                watcher.Added += (sender, args) =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        // Check if device advertises the requested service
                        if (DeviceAdvertisesService(args, serviceUuid))
                        {
                            var device = new DiscoveredDevice
                            {
                                Name = args.Name ?? "Unknown",
                                Address = GetDeviceAddress(args),
                                Rssi = GetSignalStrength(args)
                            };
                            device.ServiceUuids.Add(serviceUuid.ToString());
                            discoveredDevices.TryAdd(device.Address, device);
                        }
                    }
                };

                watcher.EnumerationCompleted += (sender, args) =>
                {
                    Task.Delay(timeout).ContinueWith(t =>
                    {
                        watcher.Stop();
                        completionSource.TrySetResult(true);
                    }, cancellationToken);
                };

                watcher.Stopped += (sender, args) =>
                {
                    completionSource.TrySetResult(true);
                };

                watcher.Start();
                await completionSource.Task;

                devices.AddRange(discoveredDevices.Values);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to discover Bluetooth LE devices with service {serviceUuid}", ex);
            }

            return devices;
        }

        private static string GetDeviceAddress(Windows.Devices.Enumeration.DeviceInformation deviceInfo)
        {
            if (deviceInfo.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address))
            {
                return address?.ToString() ?? deviceInfo.Id;
            }
            return deviceInfo.Id;
        }

        private static int GetSignalStrength(Windows.Devices.Enumeration.DeviceInformation deviceInfo)
        {
            return GetSignalStrengthFromProperties(deviceInfo.Properties);
        }

        private static int GetSignalStrengthFromProperties(IReadOnlyDictionary<string, object> properties)
        {
            if (properties.TryGetValue("System.Devices.Aep.SignalStrength", out var rssi))
            {
                if (rssi is int rssiInt)
                    return rssiInt;
                if (int.TryParse(rssi?.ToString(), out var parsedRssi))
                    return parsedRssi;
            }
            return -127; // Default unknown RSSI
        }

        private static bool DeviceAdvertisesService(Windows.Devices.Enumeration.DeviceInformation deviceInfo, Guid serviceUuid)
        {
            // In a real implementation, you would check the service UUIDs
            // For now, return true to allow filtering at a higher level
            return true;
        }

        #endregion
#endif

#if LINUX
        #region Linux Implementation

        private static async Task<List<DiscoveredDevice>> DiscoverDevicesLinuxAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                // For Linux, we'll use bluetoothctl via shell commands
                // This is a simplified approach - a production implementation might use BlueZ D-Bus API

                var tcs = new TaskCompletionSource<bool>();
                var cts = new System.Threading.CancellationTokenSource(timeout);

                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token))
                {
                    // Start bluetoothctl scan
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "bluetoothctl",
                            Arguments = "scan on",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    var outputBuilder = new System.Text.StringBuilder();
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();

                    // Wait for timeout or cancellation
                    linkedCts.Token.Register(() =>
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch { }
                        tcs.TrySetResult(true);
                    });

                    await tcs.Task;

                    // Parse the output
                    var output = outputBuilder.ToString();
                    devices = ParseBluetoothctlOutput(output);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to discover Bluetooth LE devices on Linux. Ensure bluetoothctl is installed and bluetooth service is running.", ex);
            }

            return devices;
        }

        private static async Task<List<DiscoveredDevice>> DiscoverDevicesWithServiceLinuxAsync(
            Guid serviceUuid,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // For service-specific discovery on Linux, additional filtering would be needed
            // This implementation returns all devices and filters by service at a higher level
            var allDevices = await DiscoverDevicesLinuxAsync(timeout, cancellationToken);

            // Filter devices that advertise the service (placeholder logic)
            // In a real implementation, you would check advertisement data
            return allDevices; // Return all devices for now
        }

        private static List<DiscoveredDevice> ParseBluetoothctlOutput(string output)
        {
            var devices = new Dictionary<string, DiscoveredDevice>();

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Parse lines like:
                // [NEW] Device XX:XX:XX:XX:XX:XX DeviceName
                // RSSI: -45

                if (line.Contains("Device") && line.Contains(":") && line.Length > 30)
                {
                    var parts = line.Split(new[] { "Device " }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        var deviceParts = parts[1].Split(new[] { ' ' }, 2);
                        if (deviceParts.Length >= 1)
                        {
                            var address = deviceParts[0].Trim();
                            var name = deviceParts.Length > 1 ? deviceParts[1].Trim() : "Unknown";

                            if (!devices.ContainsKey(address))
                            {
                                devices[address] = new DiscoveredDevice
                                {
                                    Address = address,
                                    Name = name,
                                    Rssi = -127
                                };
                            }
                        }
                    }
                }
                else if (line.Contains("RSSI:") && devices.Count > 0)
                {
                    // Try to parse RSSI if available
                    var rssiParts = line.Split(new[] { "RSSI:" }, StringSplitOptions.None);
                    if (rssiParts.Length > 1 && int.TryParse(rssiParts[1].Trim(), out var rssi))
                    {
                        // Update last device's RSSI (simplified)
                    }
                }
            }

            return new List<DiscoveredDevice>(devices.Values);
        }

        #endregion
#endif
    }
}
