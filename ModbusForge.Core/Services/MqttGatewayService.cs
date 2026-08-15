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

        /// <summary>True while DisconnectAsync is running; blocks late reconnects from creating a client that would never be disposed.</summary>
        private volatile bool _disconnecting;

        /// <summary>True after Dispose(); the service must not create any more clients.</summary>
        private volatile bool _disposed;

        private const int MinReconnectDelayMs = 1000;
        private const int MaxReconnectDelayMs = 30000;
        private const int DisposeWaitMs = 5000;

        public bool IsConnected => _client?.IsConnected == true;

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

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
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

            await TryConnectAsync(cancellationToken).ConfigureAwait(false);

            _reconnectTask = RunReconnectLoopAsync(_reconnectCts.Token);

            if (_settings.PublishPeriodMs > 0)
                _publishTask = RunPublishLoopAsync(_publishCts.Token);
        }

        private async Task TryConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_disposed || _disconnecting)
                    return;

                if (_client is not null && _client.IsConnected)
                    return;

                IMqttClient client;
                lock (_gate)
                {
                    if (_disposed || _disconnecting)
                        return;

                    _client?.Dispose();
                    _client = null;

                    var factory = new MqttFactory();
                    client = factory.CreateMqttClient();
                    client.DisconnectedAsync += OnDisconnectedAsync;
                    _client = client;
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

                var result = await client.ConnectAsync(options.Build(), cancellationToken).ConfigureAwait(false);
                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _logger.LogInformation("Connected to MQTT broker {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);
                }
                else
                {
                    _logger.LogWarning("MQTT broker returned {ResultCode}", result.ResultCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to connect to MQTT broker {Host}:{Port}", _settings.BrokerHost, _settings.BrokerPort);
            }
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            _logger.LogWarning("MQTT client disconnected: {Reason}", arg.Reason);
            return Task.CompletedTask;
        }

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
                        await Task.Delay(MinReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await TryConnectAsync(cancellationToken).ConfigureAwait(false);

                    if (_client?.IsConnected == true)
                    {
                        delay = MinReconnectDelayMs;
                    }
                    else
                    {
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

        public async Task DisconnectAsync()
        {
            lock (_gate)
            {
                // Block any in-flight reconnect loop from creating a new client after this
                // point (the previous code let a reconnect win the race and leak a client).
                _disconnecting = true;
                _reconnectCts?.Cancel();
                _publishCts?.Cancel();
            }

            if (_publishTask is not null)
            {
                try { await _publishTask.ConfigureAwait(false); }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogWarning(ex, "Error stopping MQTT publish loop");
                }
            }

            if (_reconnectTask is not null)
            {
                try { await _reconnectTask.ConfigureAwait(false); }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogWarning(ex, "Error stopping MQTT reconnect loop");
                }
            }

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

                client.Dispose();
            }

            lock (_gate)
            {
                _reconnectCts?.Dispose();
                _reconnectCts = null;
                _publishCts?.Dispose();
                _publishCts = null;
                _reconnectTask = null;
                _publishTask = null;
                _disconnecting = false;
            }
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
            lock (_gate)
            {
                _disposed = true;
            }

            Task? disconnectTask;
            try
            {
                disconnectTask = DisconnectAsync();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error starting MQTT disconnect during disposal");
                GC.SuppressFinalize(this);
                return;
            }

            // Bounded wait: the app must not hang on shutdown if the broker is unresponsive,
            // but we wait long enough that the client is normally disposed before exit
            // (the previous fire-and-forget Dispose let the process exit first, leaking the
            // socket).
            try
            {
                if (!disconnectTask.Wait(TimeSpan.FromMilliseconds(DisposeWaitMs)))
                {
                    _logger.LogWarning("MQTT disconnect did not complete within {TimeoutMs} ms during disposal", DisposeWaitMs);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Error during MQTT disposal");
            }

            GC.SuppressFinalize(this);
        }

        private static readonly JsonSerializerOptions MqttJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
