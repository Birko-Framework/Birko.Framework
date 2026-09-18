using System;
using System.Collections.Generic;
using System.Text;
using Birko.Communication.Ports;

namespace Birko.Communication.Hardware.Ports
{
    public class InfraportSettings : SerialSettings
    {
        public override string GetID()
        {
            return string.Format("Infraport|{0}|{1}|{2}|{3}|{4}", Name, BaudRate, Parity, DataBits, StopBits);
        }
    }

    /// <summary>
    /// Infraport implementation assuming Virtual COM Port (IrCOMM).
    /// </summary>
    public class Infraport : Serial
    {
        public Infraport(InfraportSettings settings) : base(settings)
        {
        }
    }
}
