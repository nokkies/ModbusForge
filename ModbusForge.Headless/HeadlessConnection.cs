using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Headless
{
    /// <summary>
    /// Connection management for the unattended headless runtime: retrying the
    /// initial connection and reconnecting after a drop, both with exponential
    /// backoff. An unattended poller must outlive the device's boot time and
    /// any network blip - exiting on the first failure is a restart-by-human.
    /// </summary>
    public static class HeadlessConnection
    {
        public const int InitialBackoffMs = 1000;
        public const int MaxBackoffMs = 30000;

        /// <summary>
        /// Repeatedly attempts to connect until it succeeds or the token is
        /// cancelled. The backoff starts at <paramref name="initialBackoffMs"/>
        /// and doubles on each failure, capped at <paramref name="maxBackoffMs"/>.
        /// Returns false only when cancelled.
        /// </summary>
        public static async Task<bool> EnsureConnectedAsync(
            IModbusService service,
            ConnectionProfile profile,
            ILogger logger,
            CancellationToken token,
            int initialBackoffMs = InitialBackoffMs,
            int maxBackoffMs = MaxBackoffMs)
        {
            var backoff = Math.Max(1, initialBackoffMs);

            while (!token.IsCancellationRequested)
            {
                bool connected;
                try
                {
                    connected = await service.ConnectAsync(profile, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Connection attempt failed");
                    connected = false;
                }

                if (connected)
                {
                    return true;
                }

                logger.LogWarning("Connection failed - retrying in {BackoffMs}ms", backoff);
                try
                {
                    await Task.Delay(backoff, token);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                backoff = Math.Min(backoff * 2, Math.Max(backoff, maxBackoffMs));
            }

            return false;
        }
    }
}
