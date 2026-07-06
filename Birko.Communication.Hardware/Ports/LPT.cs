using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Birko.Communication.Ports;

namespace Birko.Communication.Hardware.Ports
{
    public class LPTSettings : PortSettings
    {
        public int Number { get; set; }

        public override string GetID()
        {
            return string.Format("LPT|{0}|{1}", Name, Number);
        }
    }

    public class LPT : AbstractPort
    {
        private readonly string portname;
        private readonly int portnumber;
        private readonly int portaddress;
        //  LPT1 : hex:0x378 int:888
        //  LPT2 : hex:0x278 int:632
        //  LPT3 : hex:0x3BC int:956
        [DllImport("inpout32.dll", EntryPoint = "Out32")]
        public static extern void Output(int adress, int value);

        [DllImport("inpout32.dll", EntryPoint = "Inp32")]
        public static extern short Input(int adress);


        public LPT(LPTSettings settings) : base(settings)
        {
            portnumber = settings.Number;
            portaddress = ResolvePortAddress(settings.Number);
            portname = settings.Name;
        }

        /// <summary>
        /// Maps a logical LPT number (1/2/3) to its standard parallel-port base I/O address, which is
        /// what inpout32 Out32/Inp32 expect as their first argument. Previously the logical number was
        /// passed straight to Out32/Inp32, so writes/reads hit I/O ports 1/2 instead of the parallel
        /// data register (CR-C03). Values that are already a raw address (>= 0x100) are passed through
        /// unchanged, so a caller may specify a non-standard address directly.
        /// </summary>
        public static int ResolvePortAddress(int number)
        {
            return number switch
            {
                1 => 0x378,
                2 => 0x278,
                3 => 0x3BC,
                _ => number >= 0x100 ? number : throw new ArgumentOutOfRangeException(
                    nameof(number),
                    number,
                    "LPT number must be 1, 2 or 3, or a raw I/O address >= 0x100."),
            };
        }

        public override void Write(byte[] data)
        {
            foreach (byte d in data)
            {
               Output(portaddress, d);
            }
        }

        public override byte[] Read(int size)
        {
            List<byte> o = new List<byte>();
            for (int i = 0; i < size; i++)
            {
                o.Add((byte)Input(portaddress));
            }
            return o.ToArray();
        }

        public override void Open()
        {
            _isOpen = true;
        }

        public override void Close()
        {
            _isOpen = false;
        }

        public override bool HasReadData(int size)
        {
            return true;
        }

        public override byte[] RemoveReadData(int size)
        {
            byte[] result = Read(size);
            if (HasReadData(size))
            {
                ReadData.RemoveRange(0, size);
            }
            return result;
        }
    }
}
