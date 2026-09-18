using System;
using Birko.Communication.Ports;

namespace Birko.Communication.Bluetooth.Ports
{
    /// <summary>
    /// Settings for Bluetooth LE port communication
    /// </summary>
    public class BluetoothLESettings : PortSettings
    {
        /// <summary>
        /// Gets or sets the Bluetooth device address (MAC address in format "XX:XX:XX:XX:XX:XX")
        /// </summary>
        public string DeviceAddress { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the service UUID to connect to
        /// </summary>
        public Guid? ServiceUuid { get; set; }

        /// <summary>
        /// Gets or sets the characteristic UUID for read/write operations
        /// </summary>
        public Guid? CharacteristicUuid { get; set; }

        /// <summary>
        /// Gets or sets the connection timeout in milliseconds (default: 10000)
        /// </summary>
        public int ConnectionTimeout { get; set; } = 10000;

        /// <summary>
        /// Gets or sets whether to auto-reconnect on disconnection (default: false)
        /// </summary>
        public bool AutoReconnect { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum reconnection attempts (default: 3)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 3;

        public override string GetID()
        {
            string serviceStr = ServiceUuid?.ToString() ?? "none";
            string charStr = CharacteristicUuid?.ToString() ?? "none";
            return string.Format("BluetoothLE|{0}|{1}|{2}|{3}", Name, DeviceAddress, serviceStr, charStr);
        }
    }
}
