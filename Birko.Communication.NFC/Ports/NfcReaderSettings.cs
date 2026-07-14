using Birko.Communication.Ports;

namespace Birko.Communication.NFC.Ports
{
    /// <summary>
    /// Settings for NFC/RFID reader communication.
    /// <para>Intentionally extends <see cref="PortSettings"/> (the communication-ports base), not the
    /// Birko.Configuration <c>Settings → RemoteSettings</c> chain — consistent with the rest of
    /// Birko.Communication.Ports where every port is configured by a <see cref="PortSettings"/>. Network
    /// transports carry their target in <c>ConnectionString</c> rather than composing RemoteSettings (CR-L072).</para>
    /// </summary>
    public class NfcReaderSettings : PortSettings
    {
        /// <summary>
        /// Transport backend identifier (e.g., "serial", "http", "hid").
        /// </summary>
        public string TransportType { get; set; } = "hid";

        /// <summary>
        /// Transport-specific connection string.
        /// Serial: COM port name (e.g., "COM3", "/dev/ttyUSB0").
        /// HTTP: reader base URL (e.g., "http://192.168.1.100:8080").
        /// HID: device path or empty for auto-detect.
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Timeout in milliseconds when waiting for a tag to be presented.
        /// </summary>
        public int ReadTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Polling interval in milliseconds for continuous tag detection.
        /// </summary>
        public int PollingIntervalMs { get; set; } = 250;

        /// <summary>
        /// Whether to automatically read NDEF data when a tag is detected.
        /// </summary>
        public bool AutoReadNdef { get; set; } = true;

        /// <summary>
        /// Whether to emit TagDetected for the same UID if the tag stays on the reader.
        /// When false, TagDetected fires once until the tag is removed and re-presented.
        /// </summary>
        public bool AllowRepeatReads { get; set; } = false;

        public override string GetID()
        {
            return $"NFC|{Name}|{TransportType}|{ConnectionString}";
        }
    }
}
