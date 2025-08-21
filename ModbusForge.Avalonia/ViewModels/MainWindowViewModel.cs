using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentModbus;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ModbusForge.Avalonia.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    [ObservableProperty]
    private int _address;

    [ObservableProperty]
    private string? _value;
}

public partial class MainWindowViewModel : ViewModelBase
{
    private ModbusTcpClient? _modbusClient;

    [ObservableProperty]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    private int _port = 502;

    [ObservableProperty]
    private int _startAddress = 0;

    [ObservableProperty]
    private int _numberOfRegisters = 10;

    [ObservableProperty]
    private bool _isConnected = false;

    public ObservableCollection<RegisterViewModel> Registers { get; } = new();

    [RelayCommand]
    private async Task Connect()
    {
        if (_modbusClient is null)
        {
            _modbusClient = new ModbusTcpClient();
        }

        if (IsConnected)
        {
            _modbusClient.Disconnect();
            IsConnected = false;
        }
        else
        {
            try
            {
                _modbusClient.Connect(IpAddress, Port);
                IsConnected = true;
            }
            catch (Exception)
            {
                IsConnected = false;
            }
        }

        if (IsConnected)
        {
            await ReadDataAsync();
        }
    }

    [RelayCommand]
    private async Task ReadDataAsync()
    {
        if (_modbusClient is null || !_modbusClient.IsConnected)
        {
            return;
        }

        try
        {
            // STEP 1: Read the block of registers into a raw byte buffer.
            Memory<byte> dataBuffer = await _modbusClient.ReadHoldingRegistersAsync(
                unitIdentifier: 0,
                startingAddress: StartAddress,
                count: NumberOfRegisters);

            // STEP 2: Manually decode the raw byte buffer into an array of shorts (Big Endian).
            var decodedData = new short[NumberOfRegisters];
            for (int i = 0; i < NumberOfRegisters; i++)
            {
                if (dataBuffer.Length >= (i * 2) + 2)
                {
                    decodedData[i] = (short)((dataBuffer.Span[i * 2] << 8) | dataBuffer.Span[i * 2 + 1]);
                }
            }

            // Now you can update your UI with the decoded data
            Registers.Clear();
            int currentAddress = StartAddress;

            foreach (short value in decodedData)
            {
                Registers.Add(new RegisterViewModel
                {
                    Address = currentAddress++,
                    Value = value.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            // Handle exceptions
            Console.WriteLine($"Error reading Modbus data: {ex.Message}");
        }
    }
}
