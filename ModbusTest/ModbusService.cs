using FluentModbus;
using System;
using System.Threading.Tasks;

public class ModbusService
{
    private ModbusTcpClient? _modbusClient;

    public async Task ReadHoldingRegistersAsync(string ipAddress, int port, int startAddress, int count)
    {
        _modbusClient = new ModbusTcpClient();

        try
        {
            Console.WriteLine($"Connecting to {ipAddress}:{port}...");
            _modbusClient.Connect(ipAddress, port);
            Console.WriteLine("Connection successful.");

            // STEP 1: Read the block of registers into a raw byte buffer.
            Console.WriteLine($"Reading {count} registers from address {startAddress}...");
            Memory<byte> dataBuffer = await _modbusClient.ReadHoldingRegistersAsync(
                unitIdentifier: 0,
                startingAddress: startAddress,
                count: count);

            // STEP 2: Manually decode the raw byte buffer into an array of shorts (Big Endian).
            var decodedData = new short[count];
            for (int i = 0; i < count; i++)
            {
                if (dataBuffer.Length >= (i * 2) + 2)
                {
                    decodedData[i] = (short)((dataBuffer.Span[i * 2] << 8) | dataBuffer.Span[i * 2 + 1]);
                }
            }

            Console.WriteLine("\n--- Read successful. Decoded Data: ---");
            int currentAddress = startAddress;
            foreach (short value in decodedData)
            {
                Console.WriteLine($"Register {currentAddress++}: {value}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n--- AN ERROR OCCURRED ---");
            Console.WriteLine(ex.Message);
        }
        finally
        {
            if (_modbusClient.IsConnected)
            {
                _modbusClient.Disconnect();
                Console.WriteLine("\nConnection closed.");
            }
        }
    }
}
