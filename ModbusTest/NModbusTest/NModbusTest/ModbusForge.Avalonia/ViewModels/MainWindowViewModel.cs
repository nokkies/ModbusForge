using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NModbus;
using System;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ModbusForge.Avalonia.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    [ObservableProperty] private int _address;
    [ObservableProperty] private string? _value;
}

public partial class MainWindowViewModel : ViewModelBase
{
    private IModbusMaster? _master;
    private TcpClient? _client;

    [ObservableProperty] private string _ipAddress = "127.0.0.1";
    [ObservableProperty] private int _port = 502;
    [ObservableProperty] private int _startAddress = 0;
    [ObservableProperty] private int _numberOfRegisters = 10;
    [ObservableProperty] private bool _isConnected = false;

    public ObservableCollection<RegisterViewModel> Registers { get; } = new();

    [RelayCommand]
    private async Task Connect()
    {
        if (IsConnected)
        {
            _client?.Close();
            IsConnected = false;
        }
        else
        {
            try
            {
                _client = new TcpClient(IpAddress, Port);
                var factory = new ModbusFactory();
                _master = factory.CreateMaster(_client);
                IsConnected = true;
            }
            catch (Exception)
            {
                IsConnected = false;
            }
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ReadDataAsync()
    {
        if (!IsConnected || _master is null) return;
        try
        {
            // NModbus uses Slave ID as the first parameter
            byte slaveId = 1;
            ushort[] data = await _master.ReadHoldingRegistersAsync(slaveId, (ushort)StartAddress, (ushort)NumberOfRegisters);
            Registers.Clear();
            for (int i = 0; i < data.Length; i++)
            {
                Registers.Add(new RegisterViewModel { Address = StartAddress + i, Value = data[i].ToString() });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading Modbus data: {ex.Message}");
            // Optionally, handle the error on the UI, e.g., show a message
            // and set IsConnected to false if the connection is lost.
        }
    }
}
