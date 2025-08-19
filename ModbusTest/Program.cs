using FluentModbus.Client;
using System;
using System.Threading.Tasks;

namespace ModbusTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Modbus TCP Test Application");
            Console.WriteLine("=========================\n");

            // Default values
            string ipAddress = "127.0.0.1";
            int port = 502;

            Console.Write($"Enter server IP address [{ipAddress}]: ");
            string input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
                ipAddress = input.Trim();

            Console.Write($"Enter port number [{port}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int parsedPort))
                port = parsedPort;

            try
            {
                using var client = new ModbusTcpClient
                {
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ReadTimeout = TimeSpan.FromSeconds(5),
                    WriteTimeout = TimeSpan.FromSeconds(5)
                };

                Console.WriteLine($"\nConnecting to {ipAddress}:{port}...");
                await client.ConnectAsync(ipAddress, port);
                
                if (client.IsConnected)
                {
                    Console.WriteLine("Successfully connected to Modbus server!");
                    
                    // Try to read some registers (unit ID = 1, start address = 0, count = 10)
                    Console.WriteLine("\nReading holding registers...");
                    var registers = await client.ReadHoldingRegistersAsync(1, 0, 10);
                    
                    Console.WriteLine("Register values:");
                    for (int i = 0; i < registers.Length; i++)
                    {
                        Console.WriteLine($"Register {i}: {registers[i]}");
                    }
                }
                else
                {
                    Console.WriteLine("Failed to connect to Modbus server");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
