using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

/// <summary>
/// Exercises function-code detection against a loopback Modbus TCP listener that can be
/// told which read function codes to implement.
/// </summary>
public class ModbusDeviceProbeFunctionCodeTests : IDisposable
{
    private readonly FakeModbusServer _server = new();
    private readonly ModbusDeviceProbe _probe;

    public ModbusDeviceProbeFunctionCodeTests()
    {
        var identificationReader = new Mock<IDeviceIdentificationReader>();
        identificationReader
            .Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceIdentification?)null);

        _probe = new ModbusDeviceProbe(identificationReader.Object, new Mock<ILogger<ModbusDeviceProbe>>().Object);
    }

    private DeviceScanOptions Options() => new()
    {
        StartIpAddress = "127.0.0.1",
        EndIpAddress = "127.0.0.1",
        StartPort = _server.Port,
        EndPort = _server.Port,
        StartUnitId = 1,
        EndUnitId = 1,
        ConnectTimeoutMs = 2000,
        ResponseTimeoutMs = 2000,
        ReadDeviceIdentification = false
    };

    [Fact]
    public async Task ProbeHostAsync_ListsEveryImplementedReadFunction()
    {
        _server.Configure(new byte[] { 1, 2, 3, 4 });

        var result = Assert.Single(await _probe.ProbeHostAsync("127.0.0.1", _server.Port, Options()));

        Assert.True(result.IsDevice, $"{result.Status}: {result.Message}");
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.SupportedFunctionCodes.ToArray());
        Assert.Equal("FC01, FC02, FC03, FC04", result.SupportedFunctionCodesText);
    }

    [Fact]
    public async Task ProbeHostAsync_OmitsFunctionsRejectedAsIllegal()
    {
        // Only holding and input registers are implemented; coils/discrete inputs answer
        // with exception 0x01 Illegal Function.
        _server.Configure(new byte[] { 3, 4 });

        var result = Assert.Single(await _probe.ProbeHostAsync("127.0.0.1", _server.Port, Options()));

        Assert.Equal(new byte[] { 3, 4 }, result.SupportedFunctionCodes.ToArray());
    }

    [Fact]
    public async Task ProbeHostAsync_IllegalDataAddressStillCountsAsSupported()
    {
        // Every function is implemented but the probe address is out of range, so the unit
        // answers 0x02 Illegal Data Address - the function itself is still understood.
        _server.Configure(new byte[] { 1, 2, 3, 4 }, illegalDataAddress: true);

        var result = Assert.Single(await _probe.ProbeHostAsync("127.0.0.1", _server.Port, Options()));

        Assert.Equal(DeviceProbeStatus.RespondedWithException, result.Status);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.SupportedFunctionCodes.ToArray());
    }

    [Fact]
    public async Task ProbeHostAsync_DetectionDisabled_ReportsOnlyTheProbedFunction()
    {
        _server.Configure(new byte[] { 1, 2, 3, 4 });

        var options = Options();
        options.DetectFunctionCodes = false;

        var result = Assert.Single(await _probe.ProbeHostAsync("127.0.0.1", _server.Port, options));

        Assert.Equal(new byte[] { 3 }, result.SupportedFunctionCodes.ToArray());
    }

    [Fact]
    public async Task ProbeHostAsync_ProbedRegisterTypeDrivesTheDiscoveryFunctionCode()
    {
        _server.Configure(new byte[] { 1 });

        var options = Options();
        options.RegisterType = ScanRegisterType.Coils;
        options.DetectFunctionCodes = false;

        var result = Assert.Single(await _probe.ProbeHostAsync("127.0.0.1", _server.Port, options));

        Assert.Equal(new byte[] { 1 }, result.SupportedFunctionCodes.ToArray());
    }

    [Fact]
    public async Task ProbeHostAsync_UnreachableHost_ReportsNoFunctionCodes()
    {
        var closedPort = FakeModbusServer.GetClosedPort();
        var options = Options();
        options.StartPort = closedPort;
        options.EndPort = closedPort;
        options.ConnectTimeoutMs = 200;

        var result = Assert.Single(await _probe.ProbeHostAsync("127.0.0.1", closedPort, options));

        Assert.Equal(DeviceProbeStatus.NoTcpConnection, result.Status);
        Assert.Empty(result.SupportedFunctionCodes);
        Assert.Equal(string.Empty, result.SupportedFunctionCodesText);
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Minimal Modbus TCP listener that answers a configurable set of read function codes
    /// and rejects everything else with exception 0x01.
    /// </summary>
    private sealed class FakeModbusServer : IDisposable
    {
        private const byte IllegalFunction = 0x01;
        private const byte IllegalDataAddress = 0x02;
        private const int MbapHeaderLength = 7;

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private HashSet<byte> _supported = new();
        private bool _illegalDataAddress;

        public FakeModbusServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        /// <summary>Configures which read function codes this server implements.</summary>
        public void Configure(IEnumerable<byte> supportedFunctionCodes, bool illegalDataAddress = false)
        {
            _supported = supportedFunctionCodes.ToHashSet();
            _illegalDataAddress = illegalDataAddress;
        }

        /// <summary>A loopback port that has been bound and released, so nothing is listening on it.</summary>
        public static int GetClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // The listener was stopped.
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var header = new byte[MbapHeaderLength];

                try
                {
                    while (await ReadExactlyAsync(stream, header).ConfigureAwait(false))
                    {
                        var pduLength = ((header[4] << 8) | header[5]) - 1;
                        var pdu = new byte[pduLength];
                        if (!await ReadExactlyAsync(stream, pdu).ConfigureAwait(false)) return;

                        var response = BuildResponse(header, pdu);
                        await stream.WriteAsync(response, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException or OperationCanceledException or ObjectDisposedException)
                {
                    // The probe closed its connection.
                }
            }
        }

        private byte[] BuildResponse(byte[] header, byte[] pdu)
        {
            var functionCode = pdu[0];

            byte[] responsePdu;
            if (!_supported.Contains(functionCode))
            {
                responsePdu = new byte[] { (byte)(functionCode | 0x80), IllegalFunction };
            }
            else if (_illegalDataAddress)
            {
                responsePdu = new byte[] { (byte)(functionCode | 0x80), IllegalDataAddress };
            }
            else
            {
                // One coil/register worth of data, which satisfies a quantity-of-1 read.
                responsePdu = functionCode is 1 or 2
                    ? new byte[] { functionCode, 0x01, 0x01 }
                    : new byte[] { functionCode, 0x02, 0x00, 0x2A };
            }

            var frame = new byte[MbapHeaderLength + responsePdu.Length];
            Array.Copy(header, frame, 4);
            var length = responsePdu.Length + 1;
            frame[4] = (byte)(length >> 8);
            frame[5] = (byte)(length & 0xFF);
            frame[6] = header[6];
            Array.Copy(responsePdu, 0, frame, MbapHeaderLength, responsePdu.Length);
            return frame;
        }

        private async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), _cts.Token).ConfigureAwait(false);
                if (read == 0) return false;
                offset += read;
            }

            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
