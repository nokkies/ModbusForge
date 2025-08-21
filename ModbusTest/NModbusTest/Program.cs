using NModbus;
using System.Net.Sockets;

string ipAddress = "127.0.0.1";
int port = 502;
byte slaveId = 1;
ushort startAddress = 0;
ushort numberOfRegisters = 10;

Console.WriteLine("--- Starting NModbus Test ---");

var factory = new ModbusFactory();

try
{
    using (var client = new TcpClient(ipAddress, port))
    {
        var master = factory.CreateMaster(client);
        Console.WriteLine($"Reading {numberOfRegisters} registers from slave {slaveId} at address {startAddress}...");
        ushort[] registers = await master.ReadHoldingRegistersAsync(slaveId, startAddress, numberOfRegisters);
        Console.WriteLine("\n--- Read successful. Decoded Data: ---");
        ushort currentAddress = startAddress;
        foreach (var value in registers)
        {
            Console.WriteLine($"Register {currentAddress++}: {value}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n--- AN ERROR OCCURRED ---");
    Console.WriteLine(ex.Message);
}

Console.WriteLine("\n--- Test Finished ---");
