using System;
using System.Security.Cryptography.X509Certificates;
using Birko.Data.Stores;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT connection and client settings.
    /// Extends RemoteSettings for host/port/credentials/TLS,
    /// adding MQTT-specific options (session, keepalive, reconnect, LWT, QoS).
    /// </summary>
    public class MqttSettings : RemoteSettings, Data.Models.ILoadable<MqttSettings>
    {
        #region Properties

        /// <summary>
        /// Unique client identifier. Auto-generated if null.
        /// </summary>
        public string? ClientId { get; set; }

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

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with default values (localhost:1883).
        /// </summary>
        public MqttSettings() : base()
        {
            Location = "localhost";
            Port = 1883;
        }

        /// <summary>
        /// Initializes a new instance with host and port.
        /// </summary>
        /// <param name="host">The MQTT broker host.</param>
        /// <param name="port">The MQTT broker port.</param>
        /// <param name="username">The authentication username.</param>
        /// <param name="password">The authentication password.</param>
        /// <param name="useSsl">Whether to use TLS.</param>
        public MqttSettings(string host, int port = 1883, string? username = null, string? password = null, bool useSsl = false)
            : base(host, null!, username ?? null!, password ?? null!, port, useSsl)
        {
        }

        #endregion

        #region ISettings Implementation

        /// <inheritdoc />
        public override string GetId()
        {
            return string.Format("{0}:{1}", base.GetId(), ClientId ?? "auto");
        }

        #endregion

        #region ILoadable Implementation

        /// <summary>
        /// Loads settings from another MqttSettings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(MqttSettings data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                ClientId = data.ClientId;
                ClientCertificate = data.ClientCertificate;
                AllowUntrustedCertificates = data.AllowUntrustedCertificates;
                CleanSession = data.CleanSession;
                KeepAlive = data.KeepAlive;
                ConnectionTimeout = data.ConnectionTimeout;
                AutoReconnect = data.AutoReconnect;
                ReconnectDelay = data.ReconnectDelay;
                MaxReconnectAttempts = data.MaxReconnectAttempts;
                LastWill = data.LastWill;
                DefaultQualityOfService = data.DefaultQualityOfService;
            }
        }

        public override void LoadFrom(Settings data)
        {
            if (data is MqttSettings mqttData)
            {
                LoadFrom(mqttData);
            }
        }

        #endregion
    }
}
