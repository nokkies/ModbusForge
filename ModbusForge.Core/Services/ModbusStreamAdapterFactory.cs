using System.IO.Ports;
using System.Net.Sockets;
using NModbus.IO;

namespace ModbusForge.Services
{
    /// <summary>
    /// Creates NModbus v3 <see cref="IStreamResource"/> adapters.
    /// </summary>
    internal static class ModbusStreamAdapterFactory
    {
        public static IStreamResource CreateTcpAdapter(TcpClient tcpClient)
            => new TcpClientAdapter(tcpClient);

        public static IStreamResource CreateSerialAdapter(SerialPort serialPort)
            => new SerialPortStreamResource(serialPort);
    }
}
