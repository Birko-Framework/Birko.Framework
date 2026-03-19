using System;
using Birko.Communication.Ports;

namespace Birko.Communication.IR.Ports
{
    /// <summary>
    /// Settings for consumer IR communication via InfraredPort.
    /// </summary>
    public class InfraredSettings : PortSettings
    {
        /// <summary>
        /// Carrier frequency in Hz. Default is 38000 Hz (38 kHz), the standard for consumer IR.
        /// </summary>
        public int CarrierFrequencyHz { get; set; } = 38000;

        /// <summary>
        /// Timeout in milliseconds when waiting for a response during learning mode.
        /// </summary>
        public int ReceiveTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Transport backend identifier (e.g., "serial", "http", "mqtt", "gpio").
        /// </summary>
        public string TransportType { get; set; } = "serial";

        /// <summary>
        /// Transport-specific connection string.
        /// Serial: COM port name (e.g., "COM3").
        /// HTTP: ESPHome base URL (e.g., "http://192.168.1.100").
        /// MQTT: broker URI (e.g., "mqtt://192.168.1.1:1883").
        /// GPIO: lirc device path (e.g., "/dev/lirc0").
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        public override string GetID()
        {
            return $"IR|{Name}|{TransportType}|{ConnectionString}|{CarrierFrequencyHz}";
        }
    }
}
