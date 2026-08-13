using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using NModbus;
using NModbus.Device;

namespace ModbusForge.Services
{
    /// <summary>
    /// Splits large Modbus read and write operations into multiple protocol-sized packets
    /// and reassembles the results. Shared by <see cref="ModbusTcpService"/> and
    /// <see cref="ModbusSerialService"/>.
    /// </summary>
    internal static class ModbusChunkedExecutor
    {
        public static async Task<T[]?> ReadAsync<T>(
            Func<bool> isConnected,
            SemaphoreSlim ioLock,
            IModbusMaster? client,
            IModbusAddressValidator addressValidator,
            ILogger logger,
            Action handleConnectionLoss,
            Func<int, ushort> toProtocolAddress,
            byte unitId,
            int startAddress,
            int count,
            PlcArea area,
            string debugLogMessage,
            string errorLogContext,
            Func<IModbusMaster, ushort, ushort, T[]> readFunc)
        {
            if (!isConnected())
                return null;

            await ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    var results = new List<T>(count);

                    try
                    {
                        logger.LogDebug("{DebugMessage} (Unit ID: {UnitId})", debugLogMessage, unitId);

                        if (client == null)
                            return Array.Empty<T>();

                        var chunks = addressValidator.GetReadRanges(startAddress, count, area).ToList();

                        foreach (var chunk in chunks)
                        {
                            ushort protocolAddress = toProtocolAddress(chunk.StartAddress);
                            var chunkResult = readFunc(client, protocolAddress, (ushort)chunk.Count);

                            if (chunkResult == null || chunkResult.Length == 0)
                                break;

                            if (chunkResult.Length != chunk.Count)
                            {
                                // Slave returned fewer points than requested. Keep what we got and stop.
                                results.AddRange(chunkResult);
                                break;
                            }

                            results.AddRange(chunkResult);
                        }

                        return results.Count > 0 ? results.ToArray() : null;
                    }
                    catch (SlaveException ex)
                    {
                        logger.LogWarning(ex, "{Context}: slave returned exception code {Code}", errorLogContext, ex.SlaveExceptionCode);
                        return results.Count > 0 ? results.ToArray() : null;
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        logger.LogError(ex, errorLogContext);
                        handleConnectionLoss();
                        return results.Count > 0 ? results.ToArray() : null;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                ioLock.Release();
            }
        }

        public static async Task WriteAsync<T>(
            Func<bool> isConnected,
            SemaphoreSlim ioLock,
            IModbusMaster? client,
            IModbusAddressValidator addressValidator,
            ILogger logger,
            Action handleConnectionLoss,
            Func<int, ushort> toProtocolAddress,
            byte unitId,
            int startAddress,
            T[] values,
            PlcArea area,
            string debugLogMessage,
            string errorLogContext,
            Action<IModbusMaster, ushort, T[]> writeAction)
        {
            if (!isConnected())
                return;

            ArgumentNullException.ThrowIfNull(values);

            await ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        logger.LogDebug("{DebugMessage} (Unit ID: {UnitId})", debugLogMessage, unitId);

                        if (client == null)
                            return;

                        int max = addressValidator.GetMaxCountPerRequest(area, isWrite: true);
                        int offset = 0;

                        while (offset < values.Length)
                        {
                            int chunkCount = Math.Min(max, values.Length - offset);
                            ushort protocolAddress = toProtocolAddress(startAddress + offset);
                            var chunkValues = values.AsSpan(offset, chunkCount).ToArray();

                            writeAction(client, protocolAddress, chunkValues);
                            offset += chunkCount;
                        }
                    }
                    catch (SlaveException ex)
                    {
                        logger.LogWarning(ex, "{Context}: slave returned exception code {Code}", errorLogContext, ex.SlaveExceptionCode);
                        throw;
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        logger.LogError(ex, errorLogContext);
                        handleConnectionLoss();
                        throw;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                ioLock.Release();
            }
        }
    }
}
