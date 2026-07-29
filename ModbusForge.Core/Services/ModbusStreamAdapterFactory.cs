using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Reflection;
using Modbus.Device;
using Modbus.IO;

namespace ModbusForge.Services
{
    /// <summary>
    /// Creates NModbus4 internal <see cref="IStreamResource"/> adapters via reflection
    /// because the <c>TcpClientAdapter</c> and <c>SerialPortAdapter</c> classes are not public.
    /// </summary>
    internal static class ModbusStreamAdapterFactory
    {
        private static readonly Type? TcpClientAdapterType;
        private static readonly Type? SerialPortAdapterType;
        private static readonly ConstructorInfo? TcpClientAdapterCtor;
        private static readonly ConstructorInfo? SerialPortAdapterCtor;

        static ModbusStreamAdapterFactory()
        {
            var nmodbus = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName?.Contains("NModbus4") == true)
                ?? typeof(IModbusMaster).Assembly;

            TcpClientAdapterType = nmodbus.GetType("Modbus.IO.TcpClientAdapter", throwOnError: false, ignoreCase: false);
            SerialPortAdapterType = nmodbus.GetType("Modbus.IO.SerialPortAdapter", throwOnError: false, ignoreCase: false);

            TcpClientAdapterCtor = TcpClientAdapterType?.GetConstructor(new[] { typeof(TcpClient) });
            SerialPortAdapterCtor = SerialPortAdapterType?.GetConstructor(new[] { typeof(SerialPort) });
        }

        public static IStreamResource CreateTcpAdapter(TcpClient tcpClient)
        {
            if (TcpClientAdapterCtor is null)
                throw new InvalidOperationException("NModbus4 TcpClientAdapter is not available.");

            return (IStreamResource)TcpClientAdapterCtor.Invoke(new object[] { tcpClient });
        }

        public static IStreamResource CreateSerialAdapter(SerialPort serialPort)
        {
            if (SerialPortAdapterCtor is null)
                throw new InvalidOperationException("NModbus4 SerialPortAdapter is not available.");

            return (IStreamResource)SerialPortAdapterCtor.Invoke(new object[] { serialPort });
        }
    }
}
