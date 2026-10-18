namespace Birko.MessageQueue.Mqtt
{
    /// <summary>The MQTT protocol version a <see cref="MqttMessageQueue"/> connects with (TASK-668).</summary>
    public enum MqttProtocol
    {
        /// <summary>MQTT 3.1.1, the default. It has no message expiry and no user properties.</summary>
        V311,

        /// <summary>
        /// MQTT 5. A <see cref="QueueMessage.TimeToLive"/> is sent as the message expiry interval, and user properties carry
        /// the payload type and headers. The broker must speak MQTT 5.
        /// </summary>
        V500,
    }
}
