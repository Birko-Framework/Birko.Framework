using System;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using Birko.Communication.Hardware.Ports;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.Hardware
    /// </summary>
    public static class HardwareExamples
    {
        /// <summary>
        /// Example: Serial port communication
        /// </summary>
        public static async Task RunSerialPortExample()
        {
            ExampleOutput.WriteLine("=== Serial Port Example ===\n");

            var settings = new SerialSettings
            {
                Name = "COM3",
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One
            };

            var serial = new Serial(settings);

            serial.SubscribeProcessData(() =>
            {
                if (serial.HasReadData(0))
                {
                    var data = serial.Read(-1);
                    ExampleOutput.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
                    serial.RemoveReadData(data.Length);
                }
            });

            try
            {
                serial.Open();
                ExampleOutput.WriteLine($"Opened {settings.Name} at {settings.BaudRate} baud");

                var command = "Hello, Device!";
                serial.Write(Encoding.UTF8.GetBytes(command));
                ExampleOutput.WriteLine($"Sent: {command}");

                await Task.Delay(1000);

                var response = serial.Read(-1);
                if (response.Length > 0)
                {
                    ExampleOutput.WriteLine($"Response: {Encoding.UTF8.GetString(response)}");
                    serial.RemoveReadData(response.Length);
                }

                serial.Close();
                ExampleOutput.WriteLine("Connection closed");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Serial Port Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Parallel port (LPT) communication via inpout32.dll
        /// </summary>
        public static async Task RunParallelPortExample()
        {
            ExampleOutput.WriteLine("=== Parallel Port (LPT) Example ===\n");

            var settings = new LPTSettings
            {
                Name = "LPT1",
                Number = 0x378 // Standard LPT1 base address
            };

            var lpt = new LPT(settings);

            try
            {
                lpt.Open();
                ExampleOutput.WriteLine($"Opened {settings.Name} at address 0x{settings.Number:X3}");

                // Write a byte (all data pins high)
                byte dataToSend = 0xFF;
                lpt.Write(new[] { dataToSend });
                ExampleOutput.WriteLine($"Sent byte: 0x{dataToSend:X2}");

                await Task.Delay(500);

                // Read a byte from the port
                var received = lpt.Read(1);
                if (received.Length > 0)
                {
                    ExampleOutput.WriteLine($"Read byte: 0x{received[0]:X2}");
                }

                lpt.Close();
                ExampleOutput.WriteLine("Connection closed");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Parallel Port Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: Infrared port communication (IrCOMM via virtual COM port)
        /// </summary>
        public static async Task RunInfraredPortExample()
        {
            ExampleOutput.WriteLine("=== Infrared Port Example ===\n");

            // InfraportSettings extends SerialSettings (IrCOMM uses virtual COM port)
            var settings = new InfraportSettings
            {
                Name = "COM5",
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One
            };

            var infrared = new Infraport(settings);

            try
            {
                infrared.Open();
                ExampleOutput.WriteLine($"Opened infrared port on {settings.Name} at {settings.BaudRate} baud");

                var data = Encoding.UTF8.GetBytes("IR Data");
                infrared.Write(data);
                ExampleOutput.WriteLine("Sent data via infrared");

                await Task.Delay(2000);

                infrared.Close();
                ExampleOutput.WriteLine("Connection closed");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Infrared Port Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Example: List available serial ports on the system
        /// </summary>
        public static void ListAvailablePorts()
        {
            ExampleOutput.WriteLine("=== Available Serial Ports ===\n");

            var ports = System.IO.Ports.SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                ExampleOutput.WriteLine("  No serial ports detected.");
            }
            else
            {
                foreach (var port in ports)
                {
                    ExampleOutput.WriteLine($"  {port}");
                }
            }
        }
    }
}
