using System;

namespace Birko.EventBus.Tests.TestResources
{
    public sealed record OrderPlaced(Guid OrderId, decimal Total) : EventBase
    {
        public override string Source => "orders";
    }

    public sealed record OrderCancelled(Guid OrderId, string Reason) : EventBase
    {
        public override string Source => "orders";
    }

    public sealed record DeviceOffline(Guid DeviceId) : EventBase
    {
        public override string Source => "iot";
    }
}
