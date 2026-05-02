using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusMultiUnitServerTests : IDisposable
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly ModbusMultiUnitServer _server;

        public ModbusMultiUnitServerTests()
        {
            _loggerMock = new Mock<ILogger>();
            _server = new ModbusMultiUnitServer(_loggerMock.Object);
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public async Task HandleClientAsync_WhenNetworkExceptionOccurs_LogsDebugAndContinues()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 0);
            _server.Start(endpoint, new byte[] { 1 });
            await Task.Delay(100);

            var actualEndpoint = (IPEndPoint)_server.LocalEndpoint!;
            using var client = new TcpClient();
            await client.ConnectAsync(actualEndpoint.Address, actualEndpoint.Port);

            // Allow the server some time to accept the client and start HandleClientAsync
            await Task.Delay(100);

            // Act
            // In HandleClientAsync, ReadExactAsync swallows exceptions from stream.ReadAsync and returns false.
            // When ReadExactAsync returns false, the loop breaks gracefully without throwing into the catch block.
            // So we need an exception to happen AFTER a successful read or during write.
            // We can send a valid Modbus request, but then immediately dispose the client socket on the server side
            // or just use Reflection to close the server's network stream while it is trying to WriteAsync back.
            // Wait, actually `client.GetStream().Close()` doesn't necessarily throw an Exception in the `HandleClientAsync` catch block
            // because `ReadExactAsync` catches it internally. We must induce an error somewhere else.

            // Send a valid read request (Transaction 1, Protocol 0, Length 6, Unit 1, FC 3, Reg 0, Cnt 1)
            byte[] validRequest = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
            await client.GetStream().WriteAsync(validRequest, 0, validRequest.Length);

            // To cause `await stream.WriteAsync(response, ct)` to fail on the server, we can drop the underlying socket
            // on the client side without proper TCP closure, or simply close the client.
            // Usually, closing the client connection abruptly might cause WriteAsync to throw IOException.
            client.Client.LingerState = new LingerOption(true, 0); // Force RST (hard close)
            client.Close();

            // Wait enough time for HandleClientAsync to process and attempt WriteAsync
            await Task.Delay(200);

            // Assert
            // The method should catch the exception and log it using _logger.LogDebug
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Client connection closed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task ListenLoopAsync_WhenAcceptThrowsException_LogsErrorAndContinues()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, 0); // 0 lets OS pick a port
            _server.Start(endpoint, new byte[] { 1 });

            // Allow the server to start its listener task
            await Task.Delay(100);

            // Use reflection to access the internal _listener and stop it
            // This will cause AcceptTcpClientAsync to throw an ObjectDisposedException or SocketException
            // BUT the cancellation token in ModbusMultiUnitServer is NOT requested.
            var listenerField = typeof(ModbusMultiUnitServer).GetField("_listener", BindingFlags.NonPublic | BindingFlags.Instance);
            var listener = (TcpListener)listenerField!.GetValue(_server)!;

            // Act
            listener.Stop();

            // Wait enough time for the exception to be caught and logged
            await Task.Delay(200);

            // Assert
            // The method should catch the exception and log it using _logger.LogError
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error accepting TCP connection")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
    }
}
