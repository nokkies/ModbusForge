using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

public class DeviceScannerServiceTests
{
    private readonly Mock<ILogger<DeviceScannerService>> _logger = new();

    private static DeviceScanOptions ValidOptions() => new()
    {
        StartIpAddress = "10.0.0.1",
        EndIpAddress = "10.0.0.3",
        StartPort = 502,
        EndPort = 502,
        StartUnitId = 1,
        EndUnitId = 2
    };

    [Fact]
    public void Validate_ValidOptions_ReturnsNull()
    {
        var service = new DeviceScannerService(new FakeProbe(), _logger.Object);

        Assert.Null(service.Validate(ValidOptions()));
    }

    [Theory]
    [InlineData("nonsense", "10.0.0.1")]
    [InlineData("10.0.0.5", "10.0.0.1")]
    public void Validate_BadIpRange_ReturnsError(string start, string end)
    {
        var service = new DeviceScannerService(new FakeProbe(), _logger.Object);
        var options = ValidOptions();
        options.StartIpAddress = start;
        options.EndIpAddress = end;

        Assert.NotNull(service.Validate(options));
    }

    [Fact]
    public void Validate_UnitIdAbove247_ReturnsError()
    {
        var service = new DeviceScannerService(new FakeProbe(), _logger.Object);
        var options = ValidOptions();
        options.EndUnitId = 248;

        Assert.NotNull(service.Validate(options));
    }

    [Fact]
    public void Validate_RegisterRangePastEndOfAddressSpace_ReturnsError()
    {
        var service = new DeviceScannerService(new FakeProbe(), _logger.Object);
        var options = ValidOptions();
        options.ScanRegisterRange = true;
        options.RegisterScanStartAddress = 65530;
        options.RegisterScanCount = 100;

        Assert.NotNull(service.Validate(options));
    }

    [Fact]
    public async Task ScanAsync_InvalidOptions_Throws()
    {
        var service = new DeviceScannerService(new FakeProbe(), _logger.Object);
        var options = ValidOptions();
        options.StartPort = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => service.ScanAsync(options));
    }

    [Fact]
    public async Task ScanAsync_ProbesEveryHostInRange()
    {
        var probe = new FakeProbe();
        var service = new DeviceScannerService(probe, _logger.Object);

        await service.ScanAsync(ValidOptions());

        Assert.Equal(new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3" }, probe.ProbedHosts.Select(t => t.Host).OrderBy(h => h).ToArray());
    }

    [Fact]
    public async Task ScanAsync_ProbesEveryPortInRange()
    {
        var probe = new FakeProbe();
        var service = new DeviceScannerService(probe, _logger.Object);
        var options = ValidOptions();
        options.EndIpAddress = "10.0.0.1";
        options.StartPort = 502;
        options.EndPort = 504;

        await service.ScanAsync(options);

        Assert.Equal(new[] { 502, 503, 504 }, probe.ProbedHosts.Select(t => t.Port).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void Validate_ReversedPortRange_ReturnsError()
    {
        var service = new DeviceScannerService(new FakeProbe(), _logger.Object);
        var options = ValidOptions();
        options.StartPort = 600;
        options.EndPort = 502;

        Assert.NotNull(service.Validate(options));
    }

    [Fact]
    public async Task ScanAsync_ReportsDevicesAndProgress()
    {
        var probe = new FakeProbe
        {
            RespondingHosts = { "10.0.0.2" }
        };
        var service = new DeviceScannerService(probe, _logger.Object);
        var found = new List<DeviceScanResult>();
        var progress = new CollectingProgress();

        var results = await service.ScanAsync(ValidOptions(), progress, found.Add);
        var progressReports = progress.Reports;

        Assert.Single(found);
        Assert.Equal("10.0.0.2", found[0].IpAddress);
        Assert.Contains(results, r => r.IsDevice && r.IpAddress == "10.0.0.2");
        Assert.Equal(3, progressReports.Count);
        Assert.Equal(3, progressReports[^1].Completed);
        Assert.Equal(new[] { "10.0.0.1", "10.0.0.1", "10.0.0.2", "10.0.0.2", "10.0.0.3", "10.0.0.3" },
            results.Select(r => r.IpAddress).ToArray());
    }

    [Fact]
    public async Task ScanAsync_RespectsMaxConcurrency()
    {
        var probe = new FakeProbe { DelayMs = 20 };
        var service = new DeviceScannerService(probe, _logger.Object);
        var options = ValidOptions();
        options.StartIpAddress = "10.0.0.1";
        options.EndIpAddress = "10.0.0.16";
        options.MaxConcurrency = 2;

        await service.ScanAsync(options);

        Assert.True(probe.MaxObservedConcurrency <= 2, $"Observed concurrency {probe.MaxObservedConcurrency}");
    }

    [Fact]
    public async Task ScanAsync_Cancelled_Throws()
    {
        var probe = new FakeProbe { DelayMs = 50 };
        var service = new DeviceScannerService(probe, _logger.Object);
        var options = ValidOptions();
        options.EndIpAddress = "10.0.0.32";
        options.MaxConcurrency = 1;

        using var cts = new CancellationTokenSource(20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(options, cancellationToken: cts.Token));
    }

    private sealed class CollectingProgress : IProgress<DeviceScanProgress>
    {
        public List<DeviceScanProgress> Reports { get; } = new();

        public void Report(DeviceScanProgress value) => Reports.Add(value);
    }

    private sealed class FakeProbe : IModbusDeviceProbe
    {
        private int _currentConcurrency;

        public ConcurrentBag<(string Host, int Port)> ProbedHosts { get; } = new();
        public HashSet<string> RespondingHosts { get; } = new();
        public int DelayMs { get; init; }
        public int MaxObservedConcurrency { get; private set; }

        public async Task<IReadOnlyList<DeviceScanResult>> ProbeHostAsync(
            string ipAddress,
            int port,
            DeviceScanOptions options,
            CancellationToken cancellationToken = default)
        {
            var concurrency = Interlocked.Increment(ref _currentConcurrency);
            lock (ProbedHosts)
            {
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, concurrency);
            }

            try
            {
                ProbedHosts.Add((ipAddress, port));

                if (DelayMs > 0)
                {
                    await Task.Delay(DelayMs, cancellationToken).ConfigureAwait(false);
                }

                var results = new List<DeviceScanResult>();
                for (int unitId = options.StartUnitId; unitId <= options.EndUnitId; unitId++)
                {
                    results.Add(new DeviceScanResult
                    {
                        IpAddress = ipAddress,
                        Port = port,
                        UnitId = (byte)unitId,
                        Status = RespondingHosts.Contains(ipAddress) && unitId == options.StartUnitId
                            ? DeviceProbeStatus.Responded
                            : DeviceProbeStatus.NoModbusResponse
                    });
                }

                return results;
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }
    }
}
