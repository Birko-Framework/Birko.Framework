using Birko.Communication.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Tests;

/// <summary>
/// Hardware-free coverage for the Birko.Communication base port surface (CR-L044): PortSettings.GetID,
/// the ReadData buffer helpers, the Subscribe/Invoke/UnSubscribe event wiring, and IDisposable→Close
/// (CR-L043). Driven through a trivial in-memory AbstractPort subclass.
/// </summary>
public class AbstractPortTests
{
    private sealed class TestPort : AbstractPort
    {
        public int CloseCalls { get; private set; }

        public TestPort() { }
        public TestPort(PortSettings settings) : base(settings) { }

        public override void Open() => _isOpen = true;

        public override void Close()
        {
            CloseCalls++;
            _isOpen = false;
        }

        public override void Write(byte[] data) => ReadData.AddRange(data); // loopback for testing

        public override byte[] Read(int size)
        {
            var take = size < 0 ? ReadData.Count : System.Math.Min(size, ReadData.Count);
            return ReadData.GetRange(0, take).ToArray();
        }

        public override bool HasReadData(int size) => ReadData.Count >= size;

        public override byte[] RemoveReadData(int size)
        {
            var data = Read(size);
            ReadData.RemoveRange(0, data.Length);
            return data;
        }

        // Expose the protected notification so the wiring can be exercised (CR-L042 keeps it protected).
        public void FireProcessData() => InvokeProcessData();
    }

    [Fact]
    public void PortSettings_GetID_UsesCorrectPrefix()
    {
        // Regression for CR-L041: the prefix was misspelled "AbstratPort".
        new PortSettings { Name = "COM3" }.GetID().Should().Be("AbstractPort|COM3");
    }

    [Fact]
    public void Clear_Empties_And_IsEmpty_GetData_Reflect()
    {
        var port = new TestPort();
        port.ReadData.AddRange(new byte[] { 1, 2, 3 });

        port.IsEmpty().Should().BeFalse();
        port.GetData().Should().Equal(1, 2, 3);

        port.Clear();
        port.IsEmpty().Should().BeTrue();
        port.GetData().Should().BeEmpty();
    }

    [Fact]
    public void SubscribeProcessData_ThenInvoke_Fires_UnSubscribe_Stops()
    {
        var port = new TestPort();
        var count = 0;
        void Handler() => count++;

        port.SubscribeProcessData(Handler);
        port.FireProcessData();
        count.Should().Be(1);

        port.UnSubscribeProcessData(Handler);
        port.FireProcessData();
        count.Should().Be(1, "an unsubscribed handler must not fire");
    }

    [Fact]
    public void Dispose_ClosesThePort()
    {
        // Regression for CR-L043: IPort : IDisposable, AbstractPort.Dispose() calls Close().
        var port = new TestPort();
        port.Open();
        port.IsOpen().Should().BeTrue();

        port.Dispose();

        port.IsOpen().Should().BeFalse();
        port.CloseCalls.Should().Be(1);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var port = new TestPort();
        port.Open();

        port.Dispose();
        port.Dispose();

        port.CloseCalls.Should().Be(1, "the _disposed guard must prevent a second Close");
    }

    [Fact]
    public void UsedAsIPort_IsDisposable()
    {
        // IPort now extends IDisposable, so consumers can dispose via the interface / `using`.
        IPort port = new TestPort();
        port.Open();
        port.Dispose();
        ((TestPort)port).CloseCalls.Should().Be(1);
    }
}
