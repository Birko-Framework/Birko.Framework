using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Ports;

namespace Birko.Communication.Bluetooth.Ports
{
    /// <summary>
    /// Bluetooth LE port implementation with platform-specific support for Windows and Linux
    /// </summary>
    public class BluetoothLE : AbstractPort
    {
#if WINDOWS
        private Windows.Devices.Bluetooth.BluetoothLEDevice _device;
        private Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic _characteristic;
        private Windows.Devices.Enumeration.DeviceInformation _deviceInfo;
#elif LINUX
        private int _socket = -1;
        private BluetoothAddress _address;
#endif
        private Thread? _readThread = null;
        private bool _stopThread;
        private int _reconnectAttempts = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="BluetoothLE"/> class
        /// </summary>
        /// <param name="settings">Bluetooth LE settings</param>
        public BluetoothLE(BluetoothLESettings settings) : base(settings)
        {
        }

        /// <summary>
        /// Opens the Bluetooth LE connection
        /// </summary>
        public override void Open()
        {
            if (IsOpen()) return;

            var settings = Settings as BluetoothLESettings;
            if (settings == null)
                throw new InvalidOperationException("Invalid Settings for BluetoothLE port");

            if (string.IsNullOrEmpty(settings.DeviceAddress))
                throw new InvalidOperationException("DeviceAddress is required in BluetoothLESettings");

#if WINDOWS
            OpenWindows(settings);
#elif LINUX
            OpenLinux(settings);
#else
            throw new PlatformNotSupportedException("Bluetooth LE is not supported on this platform");
#endif
        }

#if WINDOWS
        #region Windows Implementation

        private void OpenWindows(BluetoothLESettings settings)
        {
            try
            {
                // Connect to the device
                var connectTask = ConnectWindowsAsync(settings);
                connectTask.Wait(settings.ConnectionTimeout);

                if (!_isOpen)
                {
                    throw new InvalidOperationException($"Failed to connect to Bluetooth LE device {settings.DeviceAddress}");
                }

                // Start background read thread
                _stopThread = false;
                _readThread = new Thread(ReadWorker);
                _readThread.IsBackground = true;
                _readThread.Start();

                _reconnectAttempts = 0;
            }
            catch (Exception ex)
            {
                _isOpen = false;
                throw new InvalidOperationException($"Failed to open Bluetooth LE connection: {ex.Message}", ex);
            }
        }

        private async Task ConnectWindowsAsync(BluetoothLESettings settings)
        {
            try
            {
                // Find the device by address
                string aqsFilter = $"System.Devices.Aep.DeviceAddress:=\"{settings.DeviceAddress}\"";
                string[] requestedProperties = new string[]
                {
                    "System.Devices.Aep.DeviceAddress",
                    "System.Devices.Aep.Alias"
                };

                var deviceSelector = Windows.Devices.Enumeration.DeviceInformation.CreateWatcher(
                    aqsFilter,
                    requestedProperties,
                    Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint);

                var completionSource = new TaskCompletionSource<Windows.Devices.Enumeration.DeviceInformation>();

                Windows.Devices.Enumeration.DeviceInformation foundDevice = null;

                deviceSelector.Added += (sender, args) =>
                {
                    foundDevice = args;
                    completionSource.TrySetResult(args);
                };

                deviceSelector.EnumerationCompleted += (sender, args) =>
                {
                    deviceSelector.Stop();
                    if (foundDevice == null)
                    {
                        completionSource.TrySetException(new InvalidOperationException($"Device {settings.DeviceAddress} not found"));
                    }
                };

                deviceSelector.Stopped += (sender, args) =>
                {
                    if (foundDevice == null)
                    {
                        completionSource.TrySetException(new InvalidOperationException($"Device {settings.DeviceAddress} not found"));
                    }
                };

                deviceSelector.Start();

                var deviceInfo = await completionSource.Task;
                _deviceInfo = deviceInfo;

                // Connect to the BLE device
                _device = await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceInfo.Id);

                if (_device == null)
                {
                    throw new InvalidOperationException($"Failed to connect to device {settings.DeviceAddress}");
                }

                // Get the GATT service
                if (settings.ServiceUuid.HasValue)
                {
                    var gattResult = await _device.GetGattServicesForUuidAsync(settings.ServiceUuid.Value);

                    if (gattResult.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
                    {
                        throw new InvalidOperationException($"Failed to get GATT service {settings.ServiceUuid}");
                    }

                    var services = gattResult.Services;
                    if (services.Count > 0)
                    {
                        var service = services[0];

                        // Get the characteristic
                        if (settings.CharacteristicUuid.HasValue)
                        {
                            var charResult = await service.GetCharacteristicsForUuidAsync(settings.CharacteristicUuid.Value);

                            if (charResult.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
                            {
                                throw new InvalidOperationException($"Failed to get characteristic {settings.CharacteristicUuid}");
                            }

                            var characteristics = charResult.Characteristics;
                            if (characteristics.Count > 0)
                            {
                                _characteristic = characteristics[0];

                                // Subscribe to notifications if the characteristic supports it
                                if (_characteristic.CharacteristicProperties.HasFlag(
                                    Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicProperties.Notify))
                                {
                                    await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                        Windows.Devices.Bluetooth.GenericAttributeProfile.GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                }
                            }
                        }
                    }
                }

                _isOpen = true;
            }
            catch (Exception)
            {
                _isOpen = false;
                CleanupWindows();
                throw;
            }
        }

        private void CleanupWindows()
        {
            if (_characteristic != null)
            {
                _characteristic = null;
            }

            if (_device != null)
            {
                _device.Dispose();
                _device = null;
            }

            _deviceInfo = null;
        }

        #endregion
#endif

#if LINUX
        #region Linux Implementation

        private void OpenLinux(BluetoothLESettings settings)
        {
            try
            {
                // Parse the MAC address
                _address = ParseBluetoothAddress(settings.DeviceAddress);

                // Create L2CAP socket for BLE
                _socket = socket(AF_BLUETOOTH, SOCK_SEQPACKET, BTPROTO_L2CAP);

                if (_socket < 0)
                {
                    throw new InvalidOperationException("Failed to create Bluetooth socket");
                }

                // Connect to the device
                var sockaddr = new SockaddrL2
                {
                   _family = AF_BLUETOOTH,
                    _address = _address
                };

                // Set the connection timeout
                int result = connect(_socket, ref sockaddr, sizeof(SockaddrL2));

                if (result < 0)
                {
                    CloseLinux();
                    throw new InvalidOperationException($"Failed to connect to device {settings.DeviceAddress}");
                }

                _isOpen = true;

                // Start background read thread
                _stopThread = false;
                _readThread = new Thread(ReadWorker);
                _readThread.IsBackground = true;
                _readThread.Start();

                _reconnectAttempts = 0;
            }
            catch (Exception ex)
            {
                _isOpen = false;
                CloseLinux();
                throw new InvalidOperationException($"Failed to open Bluetooth LE connection: {ex.Message}", ex);
            }
        }

        private void CloseLinux()
        {
            if (_socket >= 0)
            {
                try
                {
                    close(_socket);
                }
                catch { }
                _socket = -1;
            }
        }

        #endregion

        #region Linux Native Methods

        private const int AF_BLUETOOTH = 31;
        private const int BTPROTO_L2CAP = 0;
        private const int SOCK_SEQPACKET = 5;

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int socket(int domain, int type, int protocol);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int connect(int sockfd, ref SockaddrL2 addr, uint addrlen);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int recv(int sockfd, byte[] buf, int len, int flags);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int send(int sockfd, byte[] buf, int len, int flags);

        private struct BluetoothAddress
        {
            public byte b0, b1, b2, b3, b4, b5;
        }

        private struct SockaddrL2
        {
            public ushort _family;
            public ushort _pdevtype;
            public BluetoothAddress _address;
            public ushort _psm;
            public ushort _cid;
        }

        private static BluetoothAddress ParseBluetoothAddress(string address)
        {
            var parts = address.Split(':');
            if (parts.Length != 6)
                throw new FormatException("Invalid Bluetooth address format");

            return new BluetoothAddress
            {
                b5 = byte.Parse(parts[0], System.Globalization.NumberStyles.AllowHexSpecifier),
                b4 = byte.Parse(parts[1], System.Globalization.NumberStyles.AllowHexSpecifier),
                b3 = byte.Parse(parts[2], System.Globalization.NumberStyles.AllowHexSpecifier),
                b2 = byte.Parse(parts[3], System.Globalization.NumberStyles.AllowHexSpecifier),
                b1 = byte.Parse(parts[4], System.Globalization.NumberStyles.AllowHexSpecifier),
                b0 = byte.Parse(parts[5], System.Globalization.NumberStyles.AllowHexSpecifier)
            };
        }

        #endregion
#endif

        /// <summary>
        /// Writes data to the Bluetooth LE characteristic
        /// </summary>
        /// <param name="data">Data to write</param>
        public override void Write(byte[] data)
        {
            if (!IsOpen())
                Open();

#if WINDOWS
            WriteWindows(data);
#elif LINUX
            WriteLinux(data);
#endif
        }

#if WINDOWS
        private void WriteWindows(byte[] data)
        {
            if (_characteristic != null)
            {
                var buffer = Windows.Storage.Streams.CryptographicBuffer.CreateFromByteArray(data);
                var writeTask = _characteristic.WriteValueAsync(buffer).AsTask();

                try
                {
                    writeTask.Wait(5000); // 5 second timeout
                }
                catch (AggregateException ex) when (ex.InnerException != null)
                {
                    throw new InvalidOperationException($"Failed to write data: {ex.InnerException.Message}", ex.InnerException);
                }
            }
            else
            {
                throw new InvalidOperationException("Characteristic not available for writing");
            }
        }
#endif

#if LINUX
        private void WriteLinux(byte[] data)
        {
            if (_socket >= 0)
            {
                int sent = send(_socket, data, data.Length, 0);
                if (sent < 0)
                {
                    throw new InvalidOperationException("Failed to write data to Bluetooth socket");
                }
            }
            else
            {
                throw new InvalidOperationException("Bluetooth socket not connected");
            }
        }
#endif

        /// <summary>
        /// Reads data from the buffer
        /// </summary>
        /// <param name="size">Number of bytes to read (-1 for all available)</param>
        /// <returns>Byte array containing the data</returns>
        public override byte[] Read(int size)
        {
            if (HasReadData(size))
            {
                lock (ReadData)
                {
                    if (size < 0)
                    {
                        return ReadData.GetRange(0, ReadData.Count).ToArray();
                    }
                    else
                    {
                        return ReadData.GetRange(0, size).ToArray();
                    }
                }
            }
            return new byte[0];
        }

        /// <summary>
        /// Closes the Bluetooth LE connection
        /// </summary>
        public override void Close()
        {
            if (!IsOpen()) return;

            _stopThread = true;

            // Wait for read thread to finish
            if (_readThread != null && _readThread.IsAlive)
            {
                if (!_readThread.Join(1000))
                {
                    // Thread didn't finish in time, it will exit on its own
                }
            }

#if WINDOWS
            CleanupWindows();
#elif LINUX
            CloseLinux();
#endif

            _isOpen = false;
            Clear();
        }

        /// <summary>
        /// Checks if there is enough data in the read buffer
        /// </summary>
        /// <param name="size">Number of bytes required</param>
        /// <returns>True if enough data is available</returns>
        public override bool HasReadData(int size)
        {
            return (ReadData.Count >= size);
        }

        /// <summary>
        /// Removes data from the read buffer
        /// </summary>
        /// <param name="size">Number of bytes to remove</param>
        /// <returns>The removed data</returns>
        public override byte[] RemoveReadData(int size)
        {
            byte[] result = Read(size);
            if (HasReadData(size))
            {
                lock (ReadData)
                {
                    ReadData.RemoveRange(0, size);
                }
            }
            return result;
        }

        /// <summary>
        /// Background thread worker for reading incoming data
        /// </summary>
        private void ReadWorker()
        {
            byte[] buffer = new byte[1024];

            while (!_stopThread && IsOpen())
            {
                try
                {
#if WINDOWS
                    ReadWindowsWorker();
#elif LINUX
                    ReadLinuxWorker(buffer);
#endif
                }
                catch
                {
                    break;
                }

                Thread.Sleep(50);
            }

            // Handle auto-reconnect if connection dropped unexpectedly
            if (!_stopThread && (Settings as BluetoothLESettings)?.AutoReconnect == true)
            {
                HandleReconnect();
            }
        }

#if WINDOWS
        private void ReadWindowsWorker()
        {
            // Windows Bluetooth LE uses notifications for data
            // The characteristic_ValueChanged event would be handled here
            // For now, we poll since the event model requires more setup

            if (_characteristic != null)
            {
                // In a full implementation, you would subscribe to ValueChanged events
                // For this basic version, we just sleep and wait for notifications
            }
        }
#endif

#if LINUX
        private void ReadLinuxWorker(byte[] buffer)
        {
            if (_socket >= 0)
            {
                int received = recv(_socket, buffer, buffer.Length, 0);

                if (received > 0)
                {
                    byte[] data = new byte[received];
                    Array.Copy(buffer, data, received);

                    lock (ReadData)
                    {
                        ReadData.AddRange(data);
                    }
                    InvokeProcessData();
                }
                else if (received < 0)
                {
                    // Connection error
                    _stopThread = true;
                }
            }
        }
#endif

        private void HandleReconnect()
        {
            var settings = Settings as BluetoothLESettings;

            if (settings != null && settings.AutoReconnect && _reconnectAttempts < settings.MaxReconnectAttempts)
            {
                _reconnectAttempts++;
                _stopThread = false;

                try
                {
                    Thread.Sleep(1000 * _reconnectAttempts); // Exponential backoff
                    Open();
                }
                catch
                {
                    // Reconnect failed, will retry if attempts remain
                    _isOpen = false;
                }
            }
        }

        /// <summary>
        /// Finalizer to ensure cleanup
        /// </summary>
        ~BluetoothLE()
        {
            if (IsOpen())
            {
                Close();
            }
        }
    }
}
