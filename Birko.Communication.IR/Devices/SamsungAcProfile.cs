using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Devices
{
    /// <summary>
    /// Samsung AC mode values.
    /// </summary>
    public enum SamsungAcMode : byte
    {
        Auto = 0x00,
        Cool = 0x01,
        Dry = 0x02,
        Fan = 0x03,
        Heat = 0x04
    }

    /// <summary>
    /// Samsung AC fan speed values.
    /// </summary>
    public enum SamsungAcFan : byte
    {
        Auto = 0x00,
        Low = 0x02,
        Medium = 0x04,
        High = 0x06,
        Turbo = 0x07
    }

    /// <summary>
    /// Samsung AC swing mode.
    /// </summary>
    public enum SamsungAcSwing : byte
    {
        Off = 0x00,
        Vertical = 0x01,
        Horizontal = 0x02,
        Both = 0x03
    }

    /// <summary>
    /// Samsung AC device profile with full codebook.
    /// Samsung AC uses a 14-byte extended protocol (not standard Samsung 32-bit).
    /// Carrier: 38 kHz, similar timing to Samsung but with 3-section frame.
    /// Temperature range: 16–30 °C.
    /// Supports: modes, fan speed, swing, Wind-Free, power on/off.
    /// </summary>
    public sealed class SamsungAcProfile : IDeviceProfile
    {
        private const int MinTemp = 16;
        private const int MaxTemp = 30;
        private const int DefaultTemp = 24;
        private const byte SamsungAcAddress = 0x07;

        private const int LeaderMark = 3000;
        private const int LeaderSpace = 8900;
        private const int SectionGapMark = 3000;
        private const int SectionGapSpace = 8900;
        private const int BitMark = 500;
        private const int ZeroSpace = 500;
        private const int OneSpace = 1500;

        private readonly SamsungProtocol _baseProtocol = new();

        public string Manufacturer => "Samsung";
        public string Model => "AC (Generic)";
        public IIrProtocol Protocol => _baseProtocol;

        // Current state
        private bool _power;
        private SamsungAcMode _mode = SamsungAcMode.Cool;
        private int _temperature = DefaultTemp;
        private SamsungAcFan _fan = SamsungAcFan.Auto;
        private SamsungAcSwing _swing = SamsungAcSwing.Off;
        private bool _windFree;

        // Command registry
        private readonly Dictionary<string, Func<IrCommand>> _commands;

        public SamsungAcProfile()
        {
            _commands = new Dictionary<string, Func<IrCommand>>(StringComparer.OrdinalIgnoreCase)
            {
                ["PowerOn"] = () => { _power = true; return BuildStateCommand(); },
                ["PowerOff"] = () => { _power = false; return BuildStateCommand(); },
                ["PowerToggle"] = () => { _power = !_power; return BuildStateCommand(); },
                ["ModeCool"] = () => { _mode = SamsungAcMode.Cool; return BuildStateCommand(); },
                ["ModeHeat"] = () => { _mode = SamsungAcMode.Heat; return BuildStateCommand(); },
                ["ModeDry"] = () => { _mode = SamsungAcMode.Dry; return BuildStateCommand(); },
                ["ModeFan"] = () => { _mode = SamsungAcMode.Fan; return BuildStateCommand(); },
                ["ModeAuto"] = () => { _mode = SamsungAcMode.Auto; return BuildStateCommand(); },
                ["TempUp"] = () => { SetTemp(_temperature + 1); return BuildStateCommand(); },
                ["TempDown"] = () => { SetTemp(_temperature - 1); return BuildStateCommand(); },
                ["Temp16"] = () => { SetTemp(16); return BuildStateCommand(); },
                ["Temp18"] = () => { SetTemp(18); return BuildStateCommand(); },
                ["Temp20"] = () => { SetTemp(20); return BuildStateCommand(); },
                ["Temp22"] = () => { SetTemp(22); return BuildStateCommand(); },
                ["Temp24"] = () => { SetTemp(24); return BuildStateCommand(); },
                ["Temp26"] = () => { SetTemp(26); return BuildStateCommand(); },
                ["Temp28"] = () => { SetTemp(28); return BuildStateCommand(); },
                ["Temp30"] = () => { SetTemp(30); return BuildStateCommand(); },
                ["FanAuto"] = () => { _fan = SamsungAcFan.Auto; return BuildStateCommand(); },
                ["FanLow"] = () => { _fan = SamsungAcFan.Low; return BuildStateCommand(); },
                ["FanMedium"] = () => { _fan = SamsungAcFan.Medium; return BuildStateCommand(); },
                ["FanHigh"] = () => { _fan = SamsungAcFan.High; return BuildStateCommand(); },
                ["FanTurbo"] = () => { _fan = SamsungAcFan.Turbo; return BuildStateCommand(); },
                ["SwingOff"] = () => { _swing = SamsungAcSwing.Off; return BuildStateCommand(); },
                ["SwingVertical"] = () => { _swing = SamsungAcSwing.Vertical; return BuildStateCommand(); },
                ["SwingHorizontal"] = () => { _swing = SamsungAcSwing.Horizontal; return BuildStateCommand(); },
                ["SwingBoth"] = () => { _swing = SamsungAcSwing.Both; return BuildStateCommand(); },
                ["WindFreeOn"] = () => { _windFree = true; _fan = SamsungAcFan.Low; return BuildStateCommand(); },
                ["WindFreeOff"] = () => { _windFree = false; return BuildStateCommand(); }
            };
        }

        /// <summary>
        /// Set the desired temperature directly.
        /// </summary>
        public void SetTemperature(int celsius)
        {
            SetTemp(celsius);
        }

        /// <summary>
        /// Set mode directly.
        /// </summary>
        public void SetMode(SamsungAcMode mode)
        {
            _mode = mode;
        }

        /// <summary>
        /// Set fan speed directly.
        /// </summary>
        public void SetFanSpeed(SamsungAcFan fan)
        {
            _fan = fan;
        }

        /// <summary>
        /// Set swing mode directly.
        /// </summary>
        public void SetSwing(SamsungAcSwing swing)
        {
            _swing = swing;
        }

        /// <summary>
        /// Enable/disable Wind-Free mode.
        /// </summary>
        public void SetWindFree(bool enabled)
        {
            _windFree = enabled;
            if (enabled)
            {
                _fan = SamsungAcFan.Low;
            }
        }

        public IReadOnlyList<string> GetCommandNames()
        {
            return _commands.Keys.ToList();
        }

        public IrCommand? GetCommand(string name)
        {
            if (_commands.TryGetValue(name, out var factory))
            {
                return factory();
            }
            return null;
        }

        public IrTiming? GetTiming(string name)
        {
            var cmd = GetCommand(name);
            if (cmd == null)
            {
                return null;
            }
            return EncodeSamsungAc(cmd);
        }

        private void SetTemp(int celsius)
        {
            _temperature = Math.Clamp(celsius, MinTemp, MaxTemp);
        }

        private IrCommand BuildStateCommand()
        {
            // Build 14-byte Samsung AC frame
            var data = new byte[14];

            // Section 1 (bytes 0-6): fixed header + power
            data[0] = 0x02;
            data[1] = 0x92;
            data[2] = 0x0F;
            data[3] = 0x00;
            data[4] = 0x00;
            data[5] = 0x00;
            data[6] = 0xF0;

            // Section 2 (bytes 7-13): mode, temp, fan, swing, options
            data[7] = 0x01;
            data[8] = (byte)((_power ? 0x20 : 0x00) | ((byte)_mode & 0x0F));
            data[9] = (byte)((_temperature - 16) & 0x0F);
            data[10] = (byte)(((byte)_fan << 4) | ((byte)_swing & 0x0F));
            data[11] = (byte)(_windFree ? 0x82 : 0x00);
            data[12] = 0x00;

            // Checksum (byte 13) — XOR of bytes 7-12
            byte checksum = 0;
            for (int i = 7; i < 13; i++)
            {
                checksum ^= data[i];
            }
            data[13] = checksum;

            return new IrCommand
            {
                Protocol = "Samsung-AC",
                Address = SamsungAcAddress,
                Command = data[8],
                RawCode = BitConverter.ToUInt64(data, 0),
                BitCount = 112, // 14 bytes * 8
                ExtendedData = data
            };
        }

        /// <summary>
        /// Encode a Samsung AC command to raw IR timings.
        /// Samsung AC uses a 3-section frame with section gaps.
        /// </summary>
        private IrTiming EncodeSamsungAc(IrCommand command)
        {
            var data = command.ExtendedData;
            if (data == null || data.Length < 14)
            {
                throw new ArgumentException("Samsung AC command requires 14-byte ExtendedData.");
            }

            var durations = new List<int>();

            // Section 1: leader + bytes 0-6
            durations.Add(LeaderMark);
            durations.Add(LeaderSpace);
            EncodeBytesLsb(durations, data, 0, 7);
            durations.Add(BitMark); // stop

            // Section gap
            durations.Add(SectionGapMark);
            durations.Add(SectionGapSpace);

            // Section 2: bytes 7-13
            EncodeBytesLsb(durations, data, 7, 7);
            durations.Add(BitMark); // stop

            return new IrTiming(durations.ToArray(), 38000);
        }

        private static void EncodeBytesLsb(List<int> durations, byte[] data, int offset, int count)
        {
            for (int b = 0; b < count; b++)
            {
                byte value = data[offset + b];
                for (int bit = 0; bit < 8; bit++)
                {
                    durations.Add(BitMark);
                    bool isOne = ((value >> bit) & 1) == 1;
                    durations.Add(isOne ? OneSpace : ZeroSpace);
                }
            }
        }
    }
}
