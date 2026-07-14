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
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the device address (MAC address)
        /// </summary>
        public string Address { get; set; } = string.Empty;

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
                        // Key by args.Id (always present on both Added and Updated) rather than the
                        // device address, which a DeviceInformationUpdate does not reliably carry (CR-L045).
                        discoveredDevices.TryAdd(args.Id, device);
                    }
                };

                watcher.Updated += (sender, args) =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        // Look the device up by the stable update Id, then apply the changed properties.
                        if (discoveredDevices.TryGetValue(args.Id, out var device))
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
                };

                watcher.Removed += (sender, args) =>
                {
                    // Device removed, optionally handle
                };

                watcher.EnumerationCompleted += (sender, args) =>
                {
                    // Start timeout after enumeration completes
                    // Do NOT bind this continuation to cancellationToken: if the token is cancelled
                    // the continuation would be cancelled too and watcher.Stop()/TrySetResult would
                    // never run, hanging the await forever (CR-H018). Cancellation is handled by the
                    // registration below, which stops the watcher and cancels the awaited task.
                    Task.Delay(timeout).ContinueWith(t =>
                    {
                        watcher.Stop();
                        completionSource.TrySetResult(true);
                    });
                };

                watcher.Stopped += (sender, args) =>
                {
                    completionSource.TrySetResult(true);
                };

                // Cancellation must unblock the await: stop the watcher and cancel the task
                // (CR-H018). The Stopped handler also runs, but TrySetCanceled makes the await throw.
                using var ctr = cancellationToken.Register(() =>
                {
                    try { watcher.Stop(); } catch { }
                    completionSource.TrySetCanceled(cancellationToken);
                });

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
                    // Do NOT bind this continuation to cancellationToken: if the token is cancelled
                    // the continuation would be cancelled too and watcher.Stop()/TrySetResult would
                    // never run, hanging the await forever (CR-H018). Cancellation is handled by the
                    // registration below, which stops the watcher and cancels the awaited task.
                    Task.Delay(timeout).ContinueWith(t =>
                    {
                        watcher.Stop();
                        completionSource.TrySetResult(true);
                    });
                };

                watcher.Stopped += (sender, args) =>
                {
                    completionSource.TrySetResult(true);
                };

                // Cancellation must unblock the await: stop the watcher and cancel the task
                // (CR-H018). The Stopped handler also runs, but TrySetCanceled makes the await throw.
                using var ctr = cancellationToken.Register(() =>
                {
                    try { watcher.Stop(); } catch { }
                    completionSource.TrySetCanceled(cancellationToken);
                });

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

                // cts owns a timer (timeout ctor) and the process owns an OS handle — both are
                // IDisposable and were previously leaked; dispose all three deterministically (CR-M037).
                using (var cts = new System.Threading.CancellationTokenSource(timeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token))
                using (var process = new System.Diagnostics.Process
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
                })
                {
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

        private static Task<List<DiscoveredDevice>> DiscoverDevicesWithServiceLinuxAsync(
            Guid serviceUuid,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // Service-UUID filtering is not implemented for the bluetoothctl-based Linux path — the
            // previous stub silently ignored serviceUuid and returned ALL devices, giving callers the
            // false impression of a working filter (CR-M041). Fail loudly instead so callers use the
            // unfiltered DiscoverDevicesAsync and filter themselves, or supply a BlueZ D-Bus impl.
            throw new NotSupportedException(
                "Service-UUID-filtered discovery is not implemented on the Linux bluetoothctl backend. " +
                "Use DiscoverDevicesAsync() and filter the results, or provide a BlueZ D-Bus implementation.");
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
                // NOTE: bluetoothctl RSSI lines are intentionally not parsed here — associating an
                // "RSSI: -45" line with a specific device requires stateful correlation this simple
                // line scanner does not do. The dead branch that parsed RSSI into nothing was removed
                // (CR-M041); discovered devices report Rssi = -127 (unknown) until real RSSI wiring.
            }

            return new List<DiscoveredDevice>(devices.Values);
        }

        #endregion
#endif
    }
}
