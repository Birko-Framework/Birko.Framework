namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT Quality of Service levels.
    /// </summary>
    public enum MqttQualityOfService
    {
        /// <summary>
        /// At most once delivery. Fire and forget. No acknowledgment.
        /// Fastest but messages may be lost.
        /// </summary>
        AtMostOnce = 0,

        /// <summary>
        /// At least once delivery. Acknowledged by the receiver.
        /// Messages may be delivered more than once.
        /// </summary>
        AtLeastOnce = 1,

        /// <summary>
        /// Exactly once delivery. Four-step handshake guarantees single delivery.
        /// Slowest but most reliable.
        /// </summary>
        ExactlyOnce = 2
    }
}
