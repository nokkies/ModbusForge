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
            Func<IModbusMaster, ushort, ushort, Task<T[]?>> readFunc)
        {
            if (!isConnected())
                return null;

            await ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(async () =>
                {
                    var results = new List<T>(count);

                    try
                    {
                        logger.LogDebug("{DebugMessage} (Unit ID: {UnitId})", debugLogMessage, unitId);

                        if (client == null)
                            return null;

                        var chunks = addressValidator.GetReadRanges(startAddress, count, area).ToList();

                        foreach (var chunk in chunks)
                        {
                            ushort protocolAddress = toProtocolAddress(chunk.StartAddress);
                            var chunkResult = await readFunc(client, protocolAddress, (ushort)chunk.Count).ConfigureAwait(false);

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
            Func<IModbusMaster, ushort, T[], Task> writeAction)
        {
            if (!isConnected())
                return;

            ArgumentNullException.ThrowIfNull(values);

            await ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(async () =>
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

                            await writeAction(client, protocolAddress, chunkValues).ConfigureAwait(false);
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
