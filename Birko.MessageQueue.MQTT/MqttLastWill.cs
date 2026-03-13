namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT Last Will and Testament (LWT) configuration.
    /// Sent by the broker when the client disconnects unexpectedly.
    /// </summary>
    public class MqttLastWill
    {
        /// <summary>
        /// The topic to publish the will message to.
        /// </summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>
        /// The will message payload.
        /// </summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// QoS level for the will message. Default is AtLeastOnce.
        /// </summary>
        public MqttQualityOfService QualityOfService { get; set; } = MqttQualityOfService.AtLeastOnce;

        /// <summary>
        /// Whether the will message should be retained by the broker.
        /// </summary>
        public bool Retain { get; set; }
    }
}
