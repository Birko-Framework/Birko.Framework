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
        //  LPT1 : hex:0x378 int:888
        //  LPT2 : hex:0x278 int: 623
        [DllImport("inpout32.dll", EntryPoint = "Out32")]
        public static extern void Output(int adress, int value);

        [DllImport("inpout32.dll", EntryPoint = "Inp32")]
        public static extern short Input(int adress);


        public LPT(LPTSettings settings) : base(settings)
        {
            portnumber = settings.Number;
            portname = settings.Name;
        }

        public override void Write(byte[] data)
        {
            foreach (byte d in data)
            {
               Output(portnumber, d);
            }
        }

        public override byte[] Read(int size)
        {
            List<byte> o = new List<byte>();
            for (int i = 0; i < size; i++)
            {
                o.Add((byte)Input(portnumber));
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
