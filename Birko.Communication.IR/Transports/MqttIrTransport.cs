using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Transports
{
    /// <summary>
    /// IR transport over MQTT (ESPHome/Tasmota).
    /// Stub — requires Birko.MessageQueue.MQTT or a MQTTnet client dependency.
    /// Publish: {prefix}/cmnd/IRSend or ESPHome remote_transmitter service topic.
    /// Subscribe: {prefix}/tele/RESULT or ESPHome remote_receiver state topic.
    /// </summary>
    public class MqttIrTransport : IIrTransport
    {
        private readonly string _brokerUri;
        private readonly string _topicPrefix;

        public string Name => "MQTT";
        public bool IsConnected => false;

#pragma warning disable CS0067 // Event is never used (stub implementation)
        public event EventHandler<IrTiming>? OnReceived;
#pragma warning restore CS0067

        /// <param name="brokerUri">MQTT broker URI (e.g., "mqtt://192.168.1.1:1883").</param>
        /// <param name="topicPrefix">Topic prefix (e.g., "esphome/ir_blaster" or "tasmota/ir").</param>
        public MqttIrTransport(string brokerUri, string topicPrefix = "ir")
        {
            _brokerUri = brokerUri ?? throw new ArgumentNullException(nameof(brokerUri));
            _topicPrefix = topicPrefix;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "MQTT IR transport is a stub. Implement with MQTTnet or Birko.MessageQueue.MQTT.");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "MQTT IR transport is a stub. Implement with MQTTnet or Birko.MessageQueue.MQTT.");
        }

        public Task TransmitAsync(IrTiming timing, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "MQTT IR transport is a stub. Implement with MQTTnet or Birko.MessageQueue.MQTT.");
        }

        public Task StartReceiveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "MQTT IR transport is a stub. Implement with MQTTnet or Birko.MessageQueue.MQTT.");
        }

        public Task StopReceiveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "MQTT IR transport is a stub. Implement with MQTTnet or Birko.MessageQueue.MQTT.");
        }

        public void Dispose()
        {
        }
    }
}
