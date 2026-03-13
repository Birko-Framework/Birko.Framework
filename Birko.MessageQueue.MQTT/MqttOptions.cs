using System;
using System.Security.Cryptography.X509Certificates;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT connection and client options.
    /// </summary>
    public class MqttOptions
    {
        /// <summary>
        /// Broker hostname or IP address. Default is "localhost".
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// Broker port. Default is 1883 (unencrypted) or 8883 (TLS).
        /// </summary>
        public int Port { get; set; } = 1883;

        /// <summary>
        /// Unique client identifier. Auto-generated if null.
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Username for broker authentication.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Password for broker authentication.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Whether to use TLS/SSL. Default is false.
        /// </summary>
        public bool UseTls { get; set; }

        /// <summary>
        /// Client certificate for mutual TLS authentication.
        /// </summary>
        public X509Certificate2? ClientCertificate { get; set; }

        /// <summary>
        /// Whether to allow untrusted SSL certificates. Default is false.
        /// Only for development — do not use in production.
        /// </summary>
        public bool AllowUntrustedCertificates { get; set; }

        /// <summary>
        /// Whether to start with a clean session (no stored subscriptions/messages).
        /// When false, the broker stores subscriptions and queued messages for the client.
        /// Default is true.
        /// </summary>
        public bool CleanSession { get; set; } = true;

        /// <summary>
        /// Keep-alive interval. The client sends PINGREQ at this interval.
        /// Default is 60 seconds.
        /// </summary>
        public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Connection timeout. Default is 10 seconds.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Whether to automatically reconnect on disconnect. Default is true.
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Delay between reconnection attempts. Default is 5 seconds.
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Maximum number of reconnection attempts. 0 means unlimited. Default is 0.
        /// </summary>
        public int MaxReconnectAttempts { get; set; }

        /// <summary>
        /// Last Will and Testament configuration. Null means no LWT.
        /// </summary>
        public MqttLastWill? LastWill { get; set; }

        /// <summary>
        /// Default QoS level for publish operations. Default is AtLeastOnce.
        /// </summary>
        public MqttQualityOfService DefaultQualityOfService { get; set; } = MqttQualityOfService.AtLeastOnce;
    }
}
