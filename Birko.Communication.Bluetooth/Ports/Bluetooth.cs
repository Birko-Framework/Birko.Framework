using System;
using System.Collections.Generic;
using System.Text;
using Birko.Communication.Ports;
using Birko.Communication.Hardware.Ports;

namespace Birko.Communication.Bluetooth.Ports
{
    public class BluetoothSettings : SerialSettings
    {
        public override string GetID()
        {
            return string.Format("Bluetooth|{0}|{1}|{2}|{3}|{4}", Name, BaudRate, Parity, DataBits, StopBits);
        }
    }

    /// <summary>
    /// Bluetooth port implementation assuming Virtual COM Port.
    /// </summary>
    public class Bluetooth : Serial
    {
        public Bluetooth(BluetoothSettings settings) : base(settings)
        {
        }
    }
}
