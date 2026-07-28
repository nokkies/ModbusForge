using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using Moq;
using Xunit;

namespace ModbusForge.Tests.ViewModels;

public class DeviceScannerViewModelTests
{
    private readonly Mock<IDeviceScannerService> _scanner = new();
    private readonly Mock<IConnectionManager> _connectionManager = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<ILogger<DeviceScannerViewModel>> _logger = new();
    private readonly ObservableCollection<ConnectionProfile> _profiles = new();

    public DeviceScannerViewModelTests()
    {
        _connectionManager.SetupGet(c => c.Profiles).Returns(_profiles);
        _connectionManager.Setup(c => c.AddProfile(It.IsAny<ConnectionProfile>())).Callback<ConnectionProfile>(_profiles.Add);
    }

    private DeviceScannerViewModel CreateViewModel() => new(
        _scanner.Object,
        _connectionManager.Object,
        new ImmediateDispatcher(),
        _dialogService.Object,
        _fileDialogService.Object,
        _fileSystem.Object,
        _logger.Object);

    [Fact]
    public void BuildOptions_UsesCurrentInputs()
    {
        var viewModel = CreateViewModel();
        viewModel.StartIpAddress = "192.168.0.1";
        viewModel.EndIpAddress = "192.168.0.20";
        viewModel.StartPort = 5020;
        viewModel.EndPort = 5030;
        viewModel.StartUnitId = 3;
        viewModel.EndUnitId = 9;
        viewModel.RegisterType = ScanRegisterType.Coils;
        viewModel.ScanRegisterRange = true;
        viewModel.RegisterScanStartAddress = 100;
        viewModel.RegisterScanCount = 32;

        var options = viewModel.BuildOptions();

        Assert.Equal("192.168.0.1", options.StartIpAddress);
        Assert.Equal("192.168.0.20", options.EndIpAddress);
        Assert.Equal(5020, options.StartPort);
        Assert.Equal(5030, options.EndPort);
        Assert.Equal(3, options.StartUnitId);
        Assert.Equal(9, options.EndUnitId);
        Assert.Equal(ScanRegisterType.Coils, options.RegisterType);
        Assert.True(options.ScanRegisterRange);
        Assert.Equal(100, options.RegisterScanStartAddress);
        Assert.Equal(32, options.RegisterScanCount);
    }

    [Fact]
    public async Task StartScan_InvalidOptions_ShowsDialogAndDoesNotScan()
    {
        _scanner.Setup(s => s.Validate(It.IsAny<DeviceScanOptions>())).Returns("bad range");
        var viewModel = CreateViewModel();

        await viewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Equal("bad range", viewModel.StatusMessage);
        _dialogService.Verify(d => d.Show("bad range", It.IsAny<string>(), MessageBoxButton.OK, MessageBoxImage.Warning), Times.Once);
        _scanner.Verify(s => s.ScanAsync(It.IsAny<DeviceScanOptions>(), It.IsAny<IProgress<DeviceScanProgress>>(), It.IsAny<Action<DeviceScanResult>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartScan_AddsFoundDevicesAndReportsCompletion()
    {
        var device = new DeviceScanResult { IpAddress = "10.1.1.5", Port = 502, UnitId = 4, Status = DeviceProbeStatus.Responded };
        _scanner.Setup(s => s.Validate(It.IsAny<DeviceScanOptions>())).Returns((string?)null);
        _scanner
            .Setup(s => s.ScanAsync(It.IsAny<DeviceScanOptions>(), It.IsAny<IProgress<DeviceScanProgress>>(), It.IsAny<Action<DeviceScanResult>>(), It.IsAny<CancellationToken>()))
            .Returns((DeviceScanOptions _, IProgress<DeviceScanProgress>? _, Action<DeviceScanResult>? found, CancellationToken _) =>
            {
                found?.Invoke(device);
                return Task.FromResult<IReadOnlyList<DeviceScanResult>>(new[] { device });
            });

        var viewModel = CreateViewModel();
        await viewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Devices);
        Assert.Same(device, viewModel.Devices[0]);
        Assert.False(viewModel.IsScanning);
        Assert.Contains("Scan complete", viewModel.StatusMessage);
        Assert.True(viewModel.ExportCsvCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartScan_Cancelled_ReportsCancellation()
    {
        _scanner.Setup(s => s.Validate(It.IsAny<DeviceScanOptions>())).Returns((string?)null);
        _scanner
            .Setup(s => s.ScanAsync(It.IsAny<DeviceScanOptions>(), It.IsAny<IProgress<DeviceScanProgress>>(), It.IsAny<Action<DeviceScanResult>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var viewModel = CreateViewModel();
        await viewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Contains("cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsScanning);
    }

    [Fact]
    public async Task StartScan_Failure_ShowsErrorDialog()
    {
        _scanner.Setup(s => s.Validate(It.IsAny<DeviceScanOptions>())).Returns((string?)null);
        _scanner
            .Setup(s => s.ScanAsync(It.IsAny<DeviceScanOptions>(), It.IsAny<IProgress<DeviceScanProgress>>(), It.IsAny<Action<DeviceScanResult>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("socket exhausted"));

        var viewModel = CreateViewModel();
        await viewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Contains("socket exhausted", viewModel.StatusMessage);
        _dialogService.Verify(d => d.Show("socket exhausted", It.IsAny<string>(), MessageBoxButton.OK, MessageBoxImage.Error), Times.Once);
    }

    [Fact]
    public void BuildCsv_IncludesDeviceAndRegisterRows()
    {
        var viewModel = CreateViewModel();
        var device = new DeviceScanResult
        {
            IpAddress = "10.1.1.5",
            Port = 502,
            UnitId = 7,
            Status = DeviceProbeStatus.Responded,
            LatencyMs = 12,
            Message = "ok, responding"
        };
        device.Registers.Add(new RegisterScanResult { Address = 0, IsReadable = true, Value = 1234 });
        device.Registers.Add(new RegisterScanResult { Address = 1, IsReadable = false, Error = "illegal address" });
        viewModel.Devices.Add(device);

        var csv = viewModel.BuildCsv();
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("IpAddress,Port,UnitId,Status,LatencyMs,Message,RegisterAddress,RegisterValue", lines[0]);
        Assert.Equal("10.1.1.5,502,7,Responded,12,\"ok, responding\",,", lines[1]);
        Assert.Equal("10.1.1.5,502,7,RegisterRead,,,0,1234", lines[2]);
        Assert.Equal("10.1.1.5,502,7,RegisterUnreadable,,illegal address,1,", lines[3]);
    }

    [Fact]
    public async Task ExportCsv_NoPathChosen_DoesNotWrite()
    {
        _fileDialogService
            .Setup(f => f.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string?)null);

        var viewModel = CreateViewModel();
        viewModel.Devices.Add(new DeviceScanResult { IpAddress = "10.1.1.5", Port = 502, UnitId = 1 });

        await viewModel.ExportCsvCommand.ExecuteAsync(null);

        _fileSystem.Verify(f => f.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExportCsv_WritesFile()
    {
        _fileDialogService
            .Setup(f => f.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(@"C:\temp\scan.csv");
        _fileSystem.Setup(f => f.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var viewModel = CreateViewModel();
        viewModel.Devices.Add(new DeviceScanResult { IpAddress = "10.1.1.5", Port = 502, UnitId = 1 });

        await viewModel.ExportCsvCommand.ExecuteAsync(null);

        _fileSystem.Verify(f => f.WriteAllTextAsync(@"C:\temp\scan.csv", It.Is<string>(csv => csv.Contains("10.1.1.5"))), Times.Once);
    }

    [Fact]
    public void AddToProfiles_CreatesAndSavesProfile()
    {
        var viewModel = CreateViewModel();
        var device = new DeviceScanResult
        {
            IpAddress = "10.1.1.5",
            Port = 502,
            UnitId = 4,
            Status = DeviceProbeStatus.Responded,
            VendorName = "Acme"
        };
        viewModel.Devices.Add(device);
        viewModel.SelectedDevice = device;

        Assert.True(viewModel.AddToProfilesCommand.CanExecute(null));
        viewModel.AddToProfilesCommand.Execute(null);

        var profile = Assert.Single(_profiles);
        Assert.Equal("10.1.1.5", profile.IpAddress);
        Assert.Equal(502, profile.Port);
        Assert.Equal(4, profile.UnitId);
        Assert.Contains("Acme", profile.Name);
        _connectionManager.Verify(c => c.SaveProfiles(), Times.Once);
    }

    [Fact]
    public void AddToProfiles_DuplicateEndpoint_DoesNotAddTwice()
    {
        _profiles.Add(new ConnectionProfile("Existing", "10.1.1.5", 502, 4));
        var viewModel = CreateViewModel();
        var device = new DeviceScanResult { IpAddress = "10.1.1.5", Port = 502, UnitId = 4, Status = DeviceProbeStatus.Responded };
        viewModel.Devices.Add(device);
        viewModel.SelectedDevice = device;

        viewModel.AddToProfilesCommand.Execute(null);

        Assert.Single(_profiles);
        Assert.Contains("already saved", viewModel.StatusMessage);
        _connectionManager.Verify(c => c.SaveProfiles(), Times.Never);
    }

    [Fact]
    public void AddToProfiles_WithoutSelection_CannotExecute()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.AddToProfilesCommand.CanExecute(null));
    }

    [Fact]
    public void ClearResults_EmptiesDevicesAndDisablesExport()
    {
        var viewModel = CreateViewModel();
        viewModel.Devices.Add(new DeviceScanResult { IpAddress = "10.1.1.5", Port = 502, UnitId = 1 });

        viewModel.ClearResultsCommand.Execute(null);

        Assert.Empty(viewModel.Devices);
        Assert.False(viewModel.ExportCsvCommand.CanExecute(null));
    }
}

