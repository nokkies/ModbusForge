using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FluentModbus;

namespace ModbusForge.Services
{
    public class ModbusServerService : IModbusServer
    {
        private readonly ModbusTcpServer _server;
        private bool _disposed = false;
        private readonly ILogger<ModbusServerService> _logger;
        private bool _isRunning = false;
        private const int DefaultPort = 502;

        public ModbusServerService(ILogger<ModbusServerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _server = new ModbusTcpServer();
            _logger.LogInformation("Modbus TCP server created");
        }

        public bool IsRunning => _isRunning;

        public Task StartAsync(int port)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_isRunning)
                        return;

                    var endpoint = new IPEndPoint(IPAddress.Any, port == 0 ? DefaultPort : port);
                    _server.Start(endpoint);
                    _isRunning = true;
                    _logger.LogInformation($"Modbus TCP server started on {endpoint}");

                    // Initialize some test data in the server buffer (first few registers)
                    var buf = _server.GetHoldingRegisterBuffer<short>();
                    var initCount = Math.Min(16, buf.Length);
                    for (int i = 0; i < initCount; i++)
                        buf[i] = (short)(i * 10);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start Modbus TCP server");
                    _isRunning = false;
                }
            });
        }

        public Task StopAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_isRunning)
                    {
                        _server.Stop();
                        _isRunning = false;
                        _logger.LogInformation("Modbus TCP server stopped");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping Modbus TCP server");
                    throw;
                }
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        if (_isRunning)
                            _server.Stop();
                    }
                    catch { /* ignore on dispose */ }
                    finally
                    {
                        _server?.Dispose();
                        _isRunning = false;
                    }
                }
                _disposed = true;
            }
        }
    }
}
