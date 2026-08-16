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

                _reconnectCts = new CancellationTokenSource();
                _publishCts = new CancellationTokenSource();
            }

            _logger.LogInformation("MQTT gateway starting (broker {Host}:{Port}, publish period {Period} ms)",
                _settings.BrokerHost, _settings.BrokerPort, _settings.PublishPeriodMs);

            await TryConnectAsync(cancellationToken).ConfigureAwait(false);

            _reconnectTask = RunReconnectLoopAsync(_reconnectCts.Token);

            if (_settings.PublishPeriodMs > 0)
                _publishTask = RunPublishLoopAsync(_publishCts.Token);
        }

        private async Task TryConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_client is not null && _client.IsConnected)
                    return;

                _client?.Dispose();

                var factory = new MqttFactory();
                _client = factory.CreateMqttClient();

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
                    .WithClientId(_settings.ClientId)
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));

                if (!string.IsNullOrWhiteSpace(_settings.Username))
                {
                    options.WithCredentials(_settings.Username, _settings.Password ?? string.Empty);
                }

                _client.DisconnectedAsync += OnDisconnectedAsync;

                var result = await _client.ConnectAsync(options.Build(), cancellationToken).ConfigureAwait(false);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The reconnect loop reports the outage once per attempt; keep the
                // full exception (and its stack trace) at debug level, otherwise a
                // down broker spams a stack trace into the log every few seconds.
                _logger.LogDebug(ex, "Failed to connect to MQTT broker {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);
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

            if (_client is not null)
            {
                _client.DisconnectedAsync -= OnDisconnectedAsync;

                try
                {
                    if (_client.IsConnected)
                        await _client.DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogWarning(ex, "Error disconnecting from MQTT broker");
                }

                _client.Dispose();
                _client = null;
            }

            _reconnectCts?.Dispose();
            _reconnectCts = null;
            _publishCts?.Dispose();
            _publishCts = null;
            _reconnectTask = null;
            _publishTask = null;

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
            return _settings.TopicTemplate
                .Replace("{UnitId}", update.UnitId.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{Tag}", update.TagName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Area}", update.Area.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{Address}", update.Address.ToString(), StringComparison.OrdinalIgnoreCase);
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
