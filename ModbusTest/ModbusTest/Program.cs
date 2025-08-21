var modbusService = new ModbusService();
string ipAddress = "127.0.0.1";
int port = 502;
int startAddress = 0;
int numberOfRegisters = 10;
Console.WriteLine("--- Starting Modbus v5.3.2 Test ---");
await modbusService.ReadHoldingRegistersAsync(ipAddress, port, startAddress, numberOfRegisters);
Console.WriteLine("\n--- Test Finished ---");
