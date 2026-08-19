using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Publishes Modbus tag values to an MQTT broker.
    /// </summary>
    public class MqttGatewayService : IDisposable
    {
        private readonly ILogger<MqttGatewayService> _logger;
        private readonly object _gate = new();
        private IMqttClient? _client;
        private MqttSettings _settings = new();
        private CancellationTokenSource? _reconnectCts;
        private CancellationTokenSource? _publishCts;
        private Task? _reconnectTask;
        private Task? _publishTask;

        // Set (under _gate) as soon as a shutdown has begun. Connect attempts
        // that are still in flight when the shutdown passes check it so they
        // neither create clients the gateway no longer owns nor touch
        // CancellationTokenSources the shutdown has already disposed.
        private volatile bool _stopping;

        private const int MinReconnectDelayMs = 1000;
        private const int MaxReconnectDelayMs = 30000;

        public virtual bool IsConnected => _client?.IsConnected == true;

        /// <summary>
        /// True while the gateway is active (a connect attempt has started and it
        /// has not been fully disconnected), including the reconnect-wait period.
        /// </summary>
        public virtual bool IsRunning => _reconnectCts != null;

        /// <summary>
        /// Raised whenever the broker connection state changes (connect, drop, or
        /// full disconnect). Raised from a worker thread.
        /// </summary>
        public event EventHandler? ConnectionStateChanged;

        /// <summary>
        /// Delegate that returns the current set of tag values to publish.
        /// </summary>
        public Func<IEnumerable<MqttTagUpdate>>? SnapshotProvider { get; set; }

        public MqttGatewayService(ILogger<MqttGatewayService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ApplySettings(MqttSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
                return;

            lock (_gate)
            {
                if (_reconnectCts is not null)
                    return;

                _stopping = false;
                _reconnectCts = new CancellationTokenSource();
                _publishCts = new CancellationTokenSource();
            }

            _logger.LogInformation("MQTT gateway starting (broker {Host}:{Port}, publish period {Period} ms)",
                _settings.BrokerHost, _settings.BrokerPort, _settings.PublishPeriodMs);

            try
            {
                await TryConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The first attempt died (e.g. the token was already cancelled).
                // Without this the gateway would be left half-running: live token
                // sources, no background loop to drive them, and IsRunning stuck
                // true. Tear down and rethrow so callers see the failure.
                await DisconnectAsync().ConfigureAwait(false);
                throw;
            }

            // A DisconnectAsync may have completed while the first connect was
            // in flight: the token sources were then cancelled and disposed, so
            // they must not be used to start the background loops.
            CancellationToken loopToken;
            CancellationToken publishToken;
            bool stillRunning;
            lock (_gate)
            {
                stillRunning = !_stopping && _reconnectCts is not null && _publishCts is not null;
                loopToken = _reconnectCts?.Token ?? CancellationToken.None;
                publishToken = _publishCts?.Token ?? CancellationToken.None;
            }
            if (!stillRunning)
                return;

            _reconnectTask = RunReconnectLoopAsync(loopToken);

            if (_settings.PublishPeriodMs > 0)
                _publishTask = RunPublishLoopAsync(publishToken);
        }

        private async Task TryConnectAsync(CancellationToken cancellationToken)
        {
            IMqttClient? created = null;
            try
            {
                if (_stopping)
                    return;

                // The check-and-swap of the client reference happens under the
                // gate so a concurrent DisconnectAsync can neither see a
                // half-replaced _client nor miss the client we are about to
                // create. No await is held while the gate is taken.
                lock (_gate)
                {
                    if (_stopping)
                        return;

                    if (_client is not null && _client.IsConnected)
                        return;

                    _client?.Dispose();

                    var factory = new MqttFactory();
                    _client = factory.CreateMqttClient();
                    created = _client;
                }

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
                    .WithClientId(_settings.ClientId)
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));

                if (!string.IsNullOrWhiteSpace(_settings.Username))
                {
                    options.WithCredentials(_settings.Username, _settings.Password ?? string.Empty);
                }

                created.DisconnectedAsync += OnDisconnectedAsync;

                var result = await created.ConnectAsync(options.Build(), cancellationToken).ConfigureAwait(false);

                // The gateway was shut down while the connect was in flight:
                // DisconnectAsync already took the client (or will not, because
                // we take it first) — whichever side loses the race disposes.
                if (_stopping)
                {
                    DisposeIfOrphan(created);
                    return;
                }

                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _logger.LogInformation("Connected to MQTT broker {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);
                    RaiseConnectionStateChanged();
                }
                else
                {
                    _logger.LogWarning("MQTT broker returned {ResultCode}", result.ResultCode);
                }
            }
            catch (OperationCanceledException)
            {
                // The attempt was abandoned: the client created for it must not
                // linger (the reconnect loop will not run to dispose it).
                DisposeIfOrphan(created);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The reconnect loop reports the outage once per attempt; keep the
                // full exception (and its stack trace) at debug level, otherwise a
                // down broker spams a stack trace into the log every few seconds.
                _logger.LogDebug(ex, "Failed to connect to MQTT broker {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);
            }
        }

        /// <summary>
        /// Disposes <paramref name="client"/> when this attempt still owns it in
        /// <c>_client</c>; when a concurrent shutdown already took it out, that
        /// side is responsible for disposal. The check-and-take happens under
        /// the gate, so exactly one side disposes.
        /// </summary>
        private void DisposeIfOrphan(IMqttClient? client)
        {
            if (client is null)
                return;

            bool orphaned;
            lock (_gate)
            {
                orphaned = ReferenceEquals(_client, client);
                if (orphaned)
                    _client = null;
            }

            if (orphaned)
            {
                client.DisconnectedAsync -= OnDisconnectedAsync;
                try
                {
                    client.Dispose();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogDebug(ex, "Error disposing an orphaned MQTT client");
                }
            }
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            _logger.LogDebug("MQTT client disconnected: {Reason}", arg.Reason);
            RaiseConnectionStateChanged();
            return Task.CompletedTask;
        }

        internal void RaiseConnectionStateChanged() => ConnectionStateChanged?.Invoke(this, EventArgs.Empty);

        private async Task RunReconnectLoopAsync(CancellationToken cancellationToken)
        {
            var delay = MinReconnectDelayMs;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_client?.IsConnected == true)
                    {
                        delay = MinReconnectDelayMs;
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await TryConnectAsync(cancellationToken).ConfigureAwait(false);

                    if (_client?.IsConnected == true)
                    {
                        delay = MinReconnectDelayMs;
                    }
                    else
                    {
                        // One calm heartbeat per attempt instead of the raw
                        // exception: a down broker is an expected, self-healing
                        // condition, and the UI shows "Retrying connection...".
                        _logger.LogInformation("MQTT broker {Host}:{Port} unreachable; retrying in {Delay}s",
                            _settings.BrokerHost, _settings.BrokerPort, delay / 1000);

                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        delay = Math.Min(MaxReconnectDelayMs, delay * 2);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "MQTT reconnect loop error");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = Math.Min(MaxReconnectDelayMs, delay * 2);
                }
            }
        }

        private async Task RunPublishLoopAsync(CancellationToken cancellationToken)
        {
            var period = TimeSpan.FromMilliseconds(Math.Max(100, _settings.PublishPeriodMs));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, cancellationToken).ConfigureAwait(false);
                    await PublishSnapshotAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "MQTT publish loop error");
                }
            }
        }

        public async Task PublishSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (_client?.IsConnected != true)
                return;

            var provider = SnapshotProvider;
            if (provider is null)
                return;

            try
            {
                var updates = provider().ToList();
                if (updates.Count == 0)
                    return;

                await PublishAsync(updates, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Failed to build or publish MQTT snapshot");
            }
        }

        public virtual async Task DisconnectAsync()
        {
            lock (_gate)
            {
                // Announce the shutdown BEFORE cancelling: an in-flight connect
                // that observes this will not start background loops or keep
                // the client it created.
                _stopping = true;
                _reconnectCts?.Cancel();
                _publishCts?.Cancel();
            }

            if (_publishTask is not null)
            {
                try { await _publishTask.ConfigureAwait(false); } catch { /* ignored */ }
            }

            if (_reconnectTask is not null)
            {
                try { await _reconnectTask.ConfigureAwait(false); } catch { /* ignored */ }
            }

            // Take the client reference under the gate: an in-flight connect
            // either gives its new client to us here or disposes it itself via
            // DisposeIfOrphan — exactly one side ends up owning the disposal.
            IMqttClient? client;
            lock (_gate)
            {
                client = _client;
                _client = null;
            }

            if (client is not null)
            {
                client.DisconnectedAsync -= OnDisconnectedAsync;

                try
                {
                    if (client.IsConnected)
                        await client.DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogWarning(ex, "Error disconnecting from MQTT broker");
                }

                try
                {
                    client.Dispose();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogWarning(ex, "Error disposing the MQTT client");
                }
            }

            lock (_gate)
            {
                _reconnectCts?.Dispose();
                _reconnectCts = null;
                _publishCts?.Dispose();
                _publishCts = null;
                _reconnectTask = null;
                _publishTask = null;
            }

            RaiseConnectionStateChanged();
        }

        public async Task PublishAsync(IEnumerable<MqttTagUpdate> updates, CancellationToken cancellationToken = default)
        {
            if (_client?.IsConnected != true || updates is null)
                return;

            foreach (var update in updates)
            {
                try
                {
                    var topic = BuildTopic(update);
                    var payload = JsonSerializer.Serialize(update, MqttJsonOptions);

                    var message = new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(payload)
                        .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)Math.Clamp(_settings.QualityOfService, 0, 2))
                        .WithRetainFlag(_settings.RetainMessages)
                        .Build();

                    await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to publish MQTT message for {TagName}", update.TagName);
                }
            }
        }

        public string BuildTopic(MqttTagUpdate update)
        {
            return SanitizeTopicSegment(_settings.TopicTemplate)
                .Replace("{UnitId}", update.UnitId.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{Tag}", SanitizeTopicSegment(update.TagName), StringComparison.OrdinalIgnoreCase)
                .Replace("{Area}", update.Area.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{Address}", update.Address.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Replaces characters that are not valid in an MQTT topic name
        /// (printable ASCII 0x20-0x7E, minus the reserved filter characters
        /// '#' and '+') with '_'. Tag names are free-form text, so without this
        /// a tag like "Temp #1 (A)" would produce an invalid topic and the
        /// message would never be published.
        /// </summary>
        private static string SanitizeTopicSegment(string segment)
        {
            var chars = new char[segment.Length];
            for (var i = 0; i < segment.Length; i++)
            {
                var c = segment[i];
                chars[i] = (c >= 0x20 && c <= 0x7E && c != '#' && c != '+') ? c : '_';
            }
            return new string(chars);
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
            GC.SuppressFinalize(this);
        }

        private static readonly JsonSerializerOptions MqttJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Enum names instead of ordinals: a payload consumer must not need the
            // C# enum definition to interpret "area", and names stay stable if the
            // enum is ever reordered.
            Converters = { new JsonStringEnumConverter() },
        };
    }
}
