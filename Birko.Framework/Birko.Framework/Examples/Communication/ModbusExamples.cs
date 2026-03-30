using System;
using Birko.Communication.Modbus.Protocols;
using Birko.Communication.Network.Ports;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.Modbus
    /// </summary>
    public static class ModbusExamples
    {
        /// <summary>
        /// Example: Read holding registers via Modbus TCP
        /// </summary>
        public static void RunModbusTcpReadExample()
        {
            // Connect to Modbus TCP device (PLC, sensor gateway, etc.)
            var tcpSettings = new TcpIpSettings
            {
                Address ="192.168.1.100",
                Port = 502
            };

            var port = new TcpIp(tcpSettings);
            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            client.ResponseTimeoutMs = 5000;

            try
            {
                client.Connect();
                ExampleOutput.WriteLine("Connected to Modbus TCP device");

                // Read 10 holding registers starting at address 0 from unit 1
                var response = client.ReadHoldingRegisters(unitId: 1, startAddress: 0, quantity: 10);
                var registers = response.GetRegisters();

                for (int i = 0; i < registers.Length; i++)
                {
                    ExampleOutput.WriteLine($"  Register {i}: {registers[i]}");
                }
            }
            catch (ModbusException ex)
            {
                ExampleOutput.WriteError($"Modbus error: {ex.Message} (code: {ex.ExceptionCode})");
            }
            catch (TimeoutException ex)
            {
                ExampleOutput.WriteError($"Timeout: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Write registers via Modbus TCP
        /// </summary>
        public static void RunModbusTcpWriteExample()
        {
            var tcpSettings = new TcpIpSettings
            {
                Address ="192.168.1.100",
                Port = 502
            };

            var port = new TcpIp(tcpSettings);
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            try
            {
                client.Connect();

                // Write a single register
                client.WriteSingleRegister(unitId: 1, address: 100, value: 1234);
                ExampleOutput.WriteLine("Wrote register 100 = 1234");

                // Write multiple registers
                var values = new ushort[] { 100, 200, 300, 400 };
                client.WriteMultipleRegisters(unitId: 1, startAddress: 200, values: values);
                ExampleOutput.WriteLine($"Wrote {values.Length} registers starting at address 200");

                // Write coils
                client.WriteSingleCoil(unitId: 1, address: 0, value: true);
                ExampleOutput.WriteLine("Set coil 0 = ON");
            }
            catch (ModbusException ex)
            {
                ExampleOutput.WriteError($"Modbus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Read coils and discrete inputs
        /// </summary>
        public static void RunModbusReadCoilsExample()
        {
            var tcpSettings = new TcpIpSettings
            {
                Address ="192.168.1.100",
                Port = 502
            };

            var port = new TcpIp(tcpSettings);
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            try
            {
                client.Connect();

                // Read 8 coils starting at address 0
                var coilResponse = client.ReadCoils(unitId: 1, startAddress: 0, quantity: 8);
                var coils = coilResponse.GetCoils(8);
                for (int i = 0; i < coils.Length; i++)
                {
                    ExampleOutput.WriteLine($"  Coil {i}: {(coils[i] ? "ON" : "OFF")}");
                }

                // Read discrete inputs
                var inputResponse = client.ReadDiscreteInputs(unitId: 1, startAddress: 0, quantity: 8);
                var inputs = inputResponse.GetCoils(8);
                for (int i = 0; i < inputs.Length; i++)
                {
                    ExampleOutput.WriteLine($"  Input {i}: {(inputs[i] ? "ON" : "OFF")}");
                }
            }
            catch (ModbusException ex)
            {
                ExampleOutput.WriteError($"Modbus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Read float values from paired registers
        /// </summary>
        public static void RunModbusReadFloatsExample()
        {
            var tcpSettings = new TcpIpSettings
            {
                Address ="192.168.1.100",
                Port = 502
            };

            var port = new TcpIp(tcpSettings);
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            try
            {
                client.Connect();

                // Read 4 registers (2 floats, each using 2 registers)
                var response = client.ReadHoldingRegisters(unitId: 1, startAddress: 0, quantity: 4);

                // Decode as 32-bit floats (big-endian register pairs)
                var temperature = response.GetFloat32(0);
                var humidity = response.GetFloat32(2);

                ExampleOutput.WriteLine($"Temperature: {temperature:F1} °C");
                ExampleOutput.WriteLine($"Humidity: {humidity:F1} %");
            }
            catch (ModbusException ex)
            {
                ExampleOutput.WriteError($"Modbus error: {ex.Message}");
            }
        }
    }
}
