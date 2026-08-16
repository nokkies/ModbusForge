using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Minimal in-process MQTT 3.1.1 broker for tests: pure TCP, no dependencies.
    /// Speaks just enough of the protocol for a publisher-only client
    /// (CONNECT → CONNACK, PUBLISH/PUBACK/PUBREC/PUBCOMP, PINGREQ → PINGRESP,
    /// DISCONNECT) and records every PUBLISH it receives - topic, payload, QoS
    /// and retain flag - so tests can assert on the actual wire format.
    /// </summary>
    public sealed class FakeMqttBroker : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _gate = new();
        private readonly List<ReceivedPublish> _published = new();
        private TcpClient? _client;
        private bool _disposed;

        /// <summary>A PUBLISH received from a client, exactly as it arrived on the wire.</summary>
        public sealed record ReceivedPublish(string Topic, byte[] Payload, int QualityOfService, bool Retain, ushort PacketId);

        /// <summary>
        /// The port the broker listens on (loopback).
        /// </summary>
        public int Port { get; }

        /// <summary>How many clients have connected so far.</summary>
        public int ConnectionCount { get; private set; }

        public FakeMqttBroker()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0); // port 0: pick a free ephemeral port
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int PublishedCount
        {
            get { lock (_gate) return _published.Count; }
        }

        public IReadOnlyList<ReceivedPublish> GetAllPublished()
        {
            lock (_gate) return _published.ToList();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _client = client;
                ConnectionCount++;

                try
                {
                    await HandleClientAsync(client, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // shutting down
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    // shutting down mid-stream
                }
                catch
                {
                    // client went away; keep accepting (a reconnecting client may follow)
                }
                finally
                {
                    try { client.Dispose(); } catch { /* ignored */ }
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            var stream = client.GetStream();

            while (!token.IsCancellationRequested)
            {
                var (type, flags, remaining) = await ReadFixedHeaderAsync(stream, token).ConfigureAwait(false);

                switch (type)
                {
                    case 0x10: // CONNECT
                        {
                            await DrainAsync(stream, remaining, token).ConfigureAwait(false);
                            // CONNACK: session not present, connection accepted
                            await stream.WriteAsync(new byte[] { 0x20, 0x02, 0x00, 0x00 }, token).ConfigureAwait(false);
                            break;
                        }

                    case 0x30: // PUBLISH
                        {
                            var body = new byte[remaining];
                            await ReadExactAsync(stream, body, 0, remaining, token).ConfigureAwait(false);

                            var topicLength = (body[0] << 8) | body[1];
                            var topic = Encoding.UTF8.GetString(body, 2, topicLength);
                            var offset = 2 + topicLength;
                            var qos = (flags >> 1) & 0x03;
                            ushort packetId = 0;
                            if (qos > 0)
                            {
                                packetId = (ushort)((body[offset] << 8) | body[offset + 1]);
                                offset += 2;
                            }

                            var payload = new byte[remaining - offset];
                            Array.Copy(body, offset, payload, 0, payload.Length);

                            lock (_gate)
                            {
                                _published.Add(new ReceivedPublish(topic, payload, qos, (flags & 0x01) == 0x01, packetId));
                            }

                            if (qos == 1)
                            {
                                // PUBACK
                                await stream.WriteAsync(new byte[] { 0x40, 0x02, (byte)(packetId >> 8), (byte)packetId }, token).ConfigureAwait(false);
                            }
                            else if (qos == 2)
                            {
                                // PUBREC; the PUBCOMP reply goes out when the client's PUBREL arrives below.
                                await stream.WriteAsync(new byte[] { 0x50, 0x02, (byte)(packetId >> 8), (byte)packetId }, token).ConfigureAwait(false);
                            }

                            break;
                        }

                    case 0x60: // PUBREL (QoS 2 step 3)
                        {
                            var body = new byte[remaining];
                            await ReadExactAsync(stream, body, 0, remaining, token).ConfigureAwait(false);
                            await stream.WriteAsync(new byte[] { 0x70, 0x02, body[0], body[1] }, token).ConfigureAwait(false); // PUBCOMP
                            break;
                        }

                    case 0xC0: // PINGREQ
                        await stream.WriteAsync(new byte[] { 0xD0, 0x00 }, token).ConfigureAwait(false); // PINGRESP
                        break;

                    case 0xE0: // DISCONNECT
                        await DrainAsync(stream, remaining, token).ConfigureAwait(false);
                        return;

                    default:
                        // Anything else (SUBSCRIBE, ...) is drained and ignored:
                        // the gateway is publisher-only in the flows under test.
                        await DrainAsync(stream, remaining, token).ConfigureAwait(false);
                        break;
                }
            }
        }

        private static async Task<(byte Type, byte Flags, int Remaining)> ReadFixedHeaderAsync(NetworkStream stream, CancellationToken token)
        {
            var typeByte = await ReadByteAsync(stream, token).ConfigureAwait(false);
            var remaining = 0;
            var multiplier = 1;
            for (var i = 0; i < 4; i++)
            {
                var b = await ReadByteAsync(stream, token).ConfigureAwait(false);
                remaining += (b & 0x7F) * multiplier;
                if ((b & 0x80) == 0)
                    break;
                multiplier *= 128;
            }

            return ((byte)(typeByte & 0xF0), (byte)(typeByte & 0x0F), remaining);
        }

        private static async Task<byte> ReadByteAsync(NetworkStream stream, CancellationToken token)
        {
            var buffer = new byte[1];
            await ReadExactAsync(stream, buffer, 0, 1, token).ConfigureAwait(false);
            return buffer[0];
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            var read = 0;
            while (read < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(offset, count - read), token).ConfigureAwait(false);
                if (n == 0)
                    throw new IOException("The MQTT connection was closed.");
                read += n;
            }
        }

        private static async Task DrainAsync(NetworkStream stream, int count, CancellationToken token)
        {
            if (count <= 0)
                return;

            var buffer = new byte[Math.Min(count, 8192)];
            var remaining = count;
            while (remaining > 0)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), token).ConfigureAwait(false);
                if (n == 0)
                    throw new IOException("The MQTT connection was closed.");
                remaining -= n;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();
            try { _client?.Dispose(); } catch { /* ignored */ }
            try { _listener.Stop(); } catch { /* ignored */ }
            _cts.Dispose();
        }
    }
}
