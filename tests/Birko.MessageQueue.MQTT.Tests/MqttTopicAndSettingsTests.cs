using Birko.MessageQueue.Mqtt;
using FluentAssertions;
using MQTTnet.Protocol;
using Xunit;

namespace Birko.MessageQueue.MQTT.Tests;

/// <summary>
/// CR-M205: the pure, broker-free logic — MqttTopic validation/matching (wildcard semantics),
/// MqttProducer.ToMqttQos mapping, and MqttSettings.GetId/LoadFrom — was untested.
/// </summary>
public class MqttTopicAndSettingsTests
{
    // ── IsValidPublishTopic ──────────────────────────────
    [Theory]
    [InlineData("sensors/livingroom/temp", true)]
    [InlineData("", false)]
    [InlineData("sensors/+/temp", false)]   // wildcards not allowed when publishing
    [InlineData("sensors/#", false)]
    public void IsValidPublishTopic(string topic, bool expected)
        => MqttTopic.IsValidPublishTopic(topic).Should().Be(expected);

    // ── IsValidSubscribeFilter ───────────────────────────
    [Theory]
    [InlineData("sensors/+/temp", true)]
    [InlineData("sensors/#", true)]
    [InlineData("sensors/#/temp", false)]   // # must be the last level
    [InlineData("sensors/ab+/temp", false)] // + must stand alone in its level
    [InlineData("", false)]
    public void IsValidSubscribeFilter(string filter, bool expected)
        => MqttTopic.IsValidSubscribeFilter(filter).Should().Be(expected);

    // ── Matches ──────────────────────────────────────────
    [Theory]
    [InlineData("sensors/livingroom/temp", "sensors/livingroom/temp", true)]  // exact
    [InlineData("sensors/+/temp", "sensors/kitchen/temp", true)]              // single-level wildcard
    [InlineData("sensors/+/temp", "sensors/kitchen/humidity", false)]
    [InlineData("sensors/#", "sensors/kitchen/temp/extra", true)]            // multi-level wildcard
    [InlineData("sensors/livingroom", "sensors/livingroom/temp", false)]     // level count mismatch
    public void Matches(string filter, string topic, bool expected)
        => MqttTopic.Matches(filter, topic).Should().Be(expected);

    // ── ToMqttQos ────────────────────────────────────────
    [Theory]
    [InlineData(MqttQualityOfService.AtMostOnce, MqttQualityOfServiceLevel.AtMostOnce)]
    [InlineData(MqttQualityOfService.AtLeastOnce, MqttQualityOfServiceLevel.AtLeastOnce)]
    [InlineData(MqttQualityOfService.ExactlyOnce, MqttQualityOfServiceLevel.ExactlyOnce)]
    public void ToMqttQos_MapsEachLevel(MqttQualityOfService qos, MqttQualityOfServiceLevel expected)
        => MqttProducer.ToMqttQos(qos).Should().Be(expected);

    // ── MqttSettings ─────────────────────────────────────
    [Fact]
    public void GetId_IncludesClientId_OrAuto()
    {
        new MqttSettings { ClientId = "svc-1" }.GetId().Should().EndWith(":svc-1");
        new MqttSettings { ClientId = null }.GetId().Should().EndWith(":auto");
    }

    [Fact]
    public void LoadFrom_CopiesMqttSpecificFields()
    {
        var source = new MqttSettings
        {
            ClientId = "svc-1",
            CleanSession = false,
            KeepAlive = System.TimeSpan.FromSeconds(120),
            MaxReconnectAttempts = 9,
            DefaultQualityOfService = MqttQualityOfService.ExactlyOnce,
        };

        var target = new MqttSettings();
        target.LoadFrom(source);

        target.ClientId.Should().Be("svc-1");
        target.CleanSession.Should().BeFalse();
        target.KeepAlive.Should().Be(System.TimeSpan.FromSeconds(120));
        target.MaxReconnectAttempts.Should().Be(9);
        target.DefaultQualityOfService.Should().Be(MqttQualityOfService.ExactlyOnce);
    }
}
