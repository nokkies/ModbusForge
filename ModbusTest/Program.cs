// This simple Program.cs file will run the test.

var modbusService = new ModbusService();

// IMPORTANT: For this test to work, you need a Modbus TCP server
// running on 127.0.0.1:502. If you don't have one, this will
// throw a connection error, but a successful COMPILE is our goal.
string ipAddress = "127.0.0.1";
int port = 502;
int startAddress = 0;
int numberOfRegisters = 10;

Console.WriteLine("--- Starting Modbus v5.3.2 Test ---");
await modbusService.ReadHoldingRegistersAsync(ipAddress, port, startAddress, numberOfRegisters);
Console.WriteLine("\n--- Test Finished ---");
