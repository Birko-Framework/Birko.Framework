using Birko.Communication.Modbus.Protocols;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Modbus.Tests
{
    public class ModbusExceptionTests
    {
        [Theory]
        [InlineData(1, "Illegal function")]
        [InlineData(2, "Illegal data address")]
        [InlineData(3, "Illegal data value")]
        [InlineData(4, "Slave device failure")]
        [InlineData(5, "Acknowledge")]
        [InlineData(6, "Slave device busy")]
        public void Constructor_KnownCodes_SetsCorrectMessage(byte code, string expectedMessage)
        {
            var ex = new ModbusException(code);

            ex.ExceptionCode.Should().Be(code);
            ex.Message.Should().Contain(expectedMessage);
        }

        [Fact]
        public void Constructor_UnknownCode_SetsUnknownMessage()
        {
            var ex = new ModbusException(99);

            ex.ExceptionCode.Should().Be(99);
            ex.Message.Should().Contain("Unknown");
            ex.Message.Should().Contain("99");
        }

        [Fact]
        public void Constructor_Code0_SetsUnknownMessage()
        {
            var ex = new ModbusException(0);
            ex.ExceptionCode.Should().Be(0);
            ex.Message.Should().Contain("Unknown");
        }

        [Fact]
        public void IsException_InheritsFromSystemException()
        {
            var ex = new ModbusException(1);
            ex.Should().BeAssignableTo<System.Exception>();
        }
    }
}
