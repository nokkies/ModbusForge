using System;
using System.Windows;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;

namespace ModbusForge.Views
{
    public partial class WriteDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly MainViewModel _viewModel;
        private readonly IDialogService _dialogService;
        private readonly object _entry; // Can be RegisterEntry or CoilEntry

        public WriteDialog(MainViewModel viewModel, object entry, IDialogService? dialogService = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _dialogService = dialogService ?? new NullDialogService();
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));

            InitializeDetails();
        }

        private void InitializeDetails()
        {
            if (_entry is RegisterEntry reg)
            {
                AddressText.Text = reg.Address.ToString();
                TypeText.Text = reg.Type;
                ValueTextBox.Text = reg.ValueText;
            }
            else if (_entry is CoilEntry coil)
            {
                AddressText.Text = coil.Address.ToString();
                TypeText.Text = "Coil";
                ValueTextBox.Text = coil.State.ToString();
            }
        }

        private async void Write_Click(object sender, RoutedEventArgs e)
        {
            string rawValue = ValueTextBox.Text;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                _dialogService.Show("Please enter a value.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_entry is RegisterEntry reg)
                {
                    int address = reg.Address;
                    string type = reg.Type.ToLowerInvariant();

                    if (type == "real")
                    {
                        if (float.TryParse(rawValue, out float fVal))
                        {
                            await _viewModel.WriteFloatAtAsync(address, fVal);
                        }
                        else
                        {
                            throw new FormatException("Invalid real value format.");
                        }
                    }
                    else if (type == "string")
                    {
                        await _viewModel.WriteStringAtAsync(address, rawValue);
                    }
                    else
                    {
                        // uint or int
                        if (ushort.TryParse(rawValue, out ushort uVal))
                        {
                            await _viewModel.WriteRegisterAtAsync(address, uVal);
                        }
                        else if (int.TryParse(rawValue, out int iVal) && iVal >= 0 && iVal <= ushort.MaxValue)
                        {
                            await _viewModel.WriteRegisterAtAsync(address, (ushort)iVal);
                        }
                        else
                        {
                            throw new FormatException("Value must be a numeric integer between 0 and 65535.");
                        }
                    }
                }
                else if (_entry is CoilEntry coil)
                {
                    int address = coil.Address;
                    if (bool.TryParse(rawValue, out bool bState))
                    {
                        await _viewModel.WriteCoilAtAsync(address, bState);
                    }
                    else if (rawValue == "1" || rawValue.Equals("on", StringComparison.OrdinalIgnoreCase) || rawValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        await _viewModel.WriteCoilAtAsync(address, true);
                    }
                    else if (rawValue == "0" || rawValue.Equals("off", StringComparison.OrdinalIgnoreCase) || rawValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        await _viewModel.WriteCoilAtAsync(address, false);
                    }
                    else
                    {
                        throw new FormatException("Value must be True/False, 1/0, or On/Off.");
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _dialogService.Show($"Write failed: {ex.Message}", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
