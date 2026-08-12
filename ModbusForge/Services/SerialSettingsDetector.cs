using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Avalonia.Models;
using ModbusForge.Models;
using ModbusForge.Services;
using NModbus;

namespace ModbusForge.Avalonia.Services;

/// <summary>
/// Brute-forces common serial Modbus settings on a selected COM port and returns the first
/// configuration that produces a valid Modbus response.
/// </summary>
public sealed class SerialSettingsDetector
{
    /// <summary>
    /// Standard Modbus serial baud rates, ordered from most common to less common.
    /// </summary>
    public static IReadOnlyList<int> CommonBaudRates { get; } = new[]
    {
        9600, 19200, 38400, 57600, 115200, 4800, 2400, 1200, 14400
    };

    // Prioritized parity/data/stop combinations.
    private static readonly (Parity Parity, int DataBits, StopBits StopBits)[] CommonFrameConfigs =
    {
        (Parity.None, 8, StopBits.One),
        (Parity.Even, 8, StopBits.One),
        (Parity.Odd, 8, StopBits.One),
        (Parity.None, 7, StopBits.One),
        (Parity.None, 8, StopBits.Two),
        (Parity.Even, 7, StopBits.One),
        (Parity.Odd, 7, StopBits.One),
    };

    /// <summary>
    /// Attempts to detect the serial settings for the supplied profile.
    /// </summary>
    /// <param name="profile">The profile containing the COM port, unit ID and transport to test.</param>
    /// <param name="progress">Optional progress reporter for status messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detection result, including a log of every attempted combination.</returns>
    public async Task<SerialSettingsDetectResult> DetectAsync(
        ConnectionProfile profile,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var candidates = BuildCandidates();
        var logLines = new List<string>();
        logLines.Add($"Auto-detecting {profile.Transport} settings on {profile.ComPort}...");

        var factory = new ModbusFactory();
        var frameLogger = new ModbusFrameLogger();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var summary = $"{candidate.BaudRate}/{candidate.DataBits}/{ParityChar(candidate.Parity)}/{StopBitsChar(candidate.StopBits)}";
            var status = $"Trying {summary}...";
            progress?.Report(status);

            using var serialPort = new SerialPort(profile.ComPort, candidate.BaudRate, candidate.Parity, candidate.DataBits, candidate.StopBits)
            {
                RtsEnable = profile.RtsEnable,
                ReadTimeout = 500,
                WriteTimeout = 500,
            };

            try
            {
                serialPort.Open();
            }
            catch (Exception ex)
            {
                var openError = $"Could not open {profile.ComPort}: {ex.Message}";
                logLines.Add(openError);
                progress?.Report(openError);
                break;
            }

            var adapter = ModbusStreamAdapterFactory.CreateSerialAdapter(serialPort);
            var transport = profile.Transport == TransportType.Rtu
                ? (IModbusSerialTransport)factory.CreateRtuTransport(new LoggingStreamResource(adapter, frameLogger))
                : (IModbusSerialTransport)factory.CreateAsciiTransport(new LoggingStreamResource(adapter, frameLogger));

            using var master = factory.CreateMaster(transport);
            master.Transport.ReadTimeout = 500;
            master.Transport.WriteTimeout = 500;

            string attemptResult;
            try
            {
                await Task.Run(() => master.ReadHoldingRegisters(profile.UnitId, 0, 1), cancellationToken).ConfigureAwait(false);
                attemptResult = $"OK - {summary}";
                logLines.Add(attemptResult);

                return new SerialSettingsDetectResult
                {
                    Found = true,
                    BaudRate = candidate.BaudRate,
                    Parity = candidate.Parity,
                    DataBits = candidate.DataBits,
                    StopBits = candidate.StopBits,
                    Log = string.Join(Environment.NewLine, logLines),
                    Summary = $"Detected settings: {summary}",
                };
            }
            catch (NModbus.SlaveException slaveEx)
            {
                // A slave exception is still a valid Modbus response - the settings are correct,
                // the device just rejected the requested address/function.
                attemptResult = $"OK (slave exception {slaveEx.SlaveExceptionCode}) - {summary}";
                logLines.Add(attemptResult);

                return new SerialSettingsDetectResult
                {
                    Found = true,
                    BaudRate = candidate.BaudRate,
                    Parity = candidate.Parity,
                    DataBits = candidate.DataBits,
                    StopBits = candidate.StopBits,
                    Log = string.Join(Environment.NewLine, logLines),
                    Summary = $"Detected settings: {summary} (device responded with Modbus exception {slaveEx.SlaveExceptionCode})",
                };
            }
            catch (TimeoutException)
            {
                attemptResult = $"No response - {summary}";
                logLines.Add(attemptResult);
            }
            catch (IOException ex)
            {
                attemptResult = $"I/O error - {summary}: {ex.Message}";
                logLines.Add(attemptResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                attemptResult = $"Failed - {summary}: {ex.Message}";
                logLines.Add(attemptResult);
            }

            progress?.Report(attemptResult);
        }

        logLines.Add("No valid Modbus response found.");

        return new SerialSettingsDetectResult
        {
            Found = false,
            Log = string.Join(Environment.NewLine, logLines),
            Summary = $"Could not detect {profile.Transport} settings on {profile.ComPort}.",
        };
    }

    private static List<(int BaudRate, Parity Parity, int DataBits, StopBits StopBits)> BuildCandidates()
    {
        var candidates = new List<(int, Parity, int, StopBits)>();

        foreach (var baud in CommonBaudRates)
        {
            foreach (var (parity, dataBits, stopBits) in CommonFrameConfigs)
            {
                candidates.Add((baud, parity, dataBits, stopBits));
            }
        }

        return candidates;
    }

    private static char ParityChar(Parity parity) => parity switch
    {
        Parity.Even => 'E',
        Parity.Odd => 'O',
        Parity.Mark => 'M',
        Parity.Space => 'S',
        _ => 'N',
    };

    private static string StopBitsChar(StopBits stopBits) => stopBits switch
    {
        StopBits.One => "1",
        StopBits.OnePointFive => "1.5",
        StopBits.Two => "2",
        _ => "0",
    };
}
