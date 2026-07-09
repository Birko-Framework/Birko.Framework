using System;
using System.IO.Ports;
using Birko.Communication.Hardware.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Hardware.Tests
{
    public class SerialTests
    {
        // ---- GetID formatting -------------------------------------------------

        [Fact]
        public void SerialSettings_GetID_formats_all_fields()
        {
            var settings = new SerialSettings
            {
                Name = "COM3",
                BaudRate = 19200,
                Parity = Parity.Even,
                DataBits = 8,
                StopBits = StopBits.One,
            };

            settings.GetID().Should().Be("SerialPort|COM3|19200|Even|8|One");
        }

        [Fact]
        public void InfraportSettings_GetID_formats_all_fields()
        {
            var settings = new InfraportSettings
            {
                Name = "COM5",
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 7,
                StopBits = StopBits.Two,
            };

            settings.GetID().Should().Be("Infraport|COM5|9600|None|7|Two");
        }

        [Fact]
        public void LPTSettings_GetID_formats_all_fields()
        {
            var settings = new LPTSettings
            {
                Name = "LPT1",
                Number = 1,
            };

            settings.GetID().Should().Be("LPT|LPT1|1");
        }

        // ---- Constructor guard (CR-M048) -------------------------------------

        [Fact]
        public void Constructor_with_null_settings_throws_ArgumentNullException()
        {
            var act = () => new Serial(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        // ---- Buffer logic without opening the port ---------------------------

        private static Serial NewSerialWithBuffer(params byte[] seed)
        {
            // DataBits/StopBits must be valid for SerialPort's ctor (DataBits 5-8); the buffer
            // logic under test never opens the port, so these only satisfy the ctor.
            var serial = new Serial(new SerialSettings { Name = "COM1", BaudRate = 9600, DataBits = 8, StopBits = StopBits.One });
            serial.ReadData.AddRange(seed);
            return serial;
        }

        [Fact]
        public void Read_returns_first_bytes_and_is_non_destructive()
        {
            var serial = NewSerialWithBuffer(1, 2, 3, 4);

            serial.Read(2).Should().Equal(new byte[] { 1, 2 });
            // Non-destructive: buffer is untouched.
            serial.ReadData.Should().Equal(new byte[] { 1, 2, 3, 4 });
        }

        [Fact]
        public void Read_minus_one_returns_all_bytes()
        {
            var serial = NewSerialWithBuffer(9, 8, 7);

            serial.Read(-1).Should().Equal(new byte[] { 9, 8, 7 });
        }

        [Fact]
        public void Read_minus_one_on_empty_buffer_returns_empty()
        {
            var serial = NewSerialWithBuffer();

            serial.Read(-1).Should().BeEmpty();
        }

        [Fact]
        public void Read_with_size_greater_than_count_returns_empty()
        {
            var serial = NewSerialWithBuffer(1, 2);

            serial.Read(5).Should().BeEmpty();
        }

        [Fact]
        public void HasReadData_reflects_buffer_count()
        {
            var serial = NewSerialWithBuffer(1, 2, 3);

            serial.HasReadData(3).Should().BeTrue();
            serial.HasReadData(2).Should().BeTrue();
            serial.HasReadData(4).Should().BeFalse();
        }

        [Fact]
        public void HasReadData_minus_one_is_true_only_when_non_empty()
        {
            NewSerialWithBuffer(1).HasReadData(-1).Should().BeTrue();
            NewSerialWithBuffer().HasReadData(-1).Should().BeFalse();
        }

        [Fact]
        public void RemoveReadData_returns_and_removes_first_bytes()
        {
            var serial = NewSerialWithBuffer(1, 2, 3, 4);

            serial.RemoveReadData(2).Should().Equal(new byte[] { 1, 2 });
            serial.ReadData.Should().Equal(new byte[] { 3, 4 });
        }

        [Fact]
        public void RemoveReadData_minus_one_drains_all_and_does_not_throw()
        {
            var serial = NewSerialWithBuffer(1, 2, 3);

            byte[] removed = new byte[0];
            var act = () => removed = serial.RemoveReadData(-1);

            act.Should().NotThrow();
            removed.Should().Equal(new byte[] { 1, 2, 3 });
            serial.ReadData.Should().BeEmpty();
        }

        [Fact]
        public void RemoveReadData_on_empty_buffer_returns_empty()
        {
            var serial = NewSerialWithBuffer();

            serial.RemoveReadData(2).Should().BeEmpty();
        }
    }
}
