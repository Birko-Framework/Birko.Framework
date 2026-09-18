using System;

namespace Birko.Communication.Modbus.Protocols
{
    public class ModbusException : Exception
    {
        public byte ExceptionCode { get; }

        public ModbusException(byte exceptionCode)
            : base(GetMessage(exceptionCode))
        {
            ExceptionCode = exceptionCode;
        }

        private static string GetMessage(byte code) => code switch
        {
            1 => "Illegal function",
            2 => "Illegal data address",
            3 => "Illegal data value",
            4 => "Slave device failure",
            5 => "Acknowledge — request accepted, processing",
            6 => "Slave device busy",
            _ => $"Unknown Modbus exception code: {code}"
        };
    }
}
