using System;
using System.Security.Cryptography;
using System.Windows;
using ModbusForge.Services;
using ModbusForge.ViewModels;

namespace ModbusForge;

public partial class PreferencesWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly PreferencesViewModel _viewModel;

    public PreferencesWindow(PreferencesViewModel viewModel)
    {
        if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
        Loaded += OnLoaded;
    }

    private void OnRequestClose(object? sender, bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility();
    }

    private void EnableApiCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility();
    }

    private void EnableApiAuthenticationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility();
        if (EnableApiAuthenticationCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(_viewModel.ApiKey))
        {
            _viewModel.ApiKey = GenerateApiKey();
        }
    }

    private void CopyApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_viewModel.ApiKey))
        {
            Clipboard.SetText(_viewModel.ApiKey);
        }
    }

    private void RegenerateApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ApiKey = GenerateApiKey();
    }

    private void UpdateApiKeyVisibility()
    {
        bool apiEnabled = EnableApiCheckBox.IsChecked == true;
        bool authEnabled = EnableApiAuthenticationCheckBox.IsChecked == true;

        EnableApiAuthenticationCheckBox.IsEnabled = apiEnabled;
        ApiKeyTextBox.IsEnabled = apiEnabled;
        CopyApiKeyButton.IsEnabled = apiEnabled && !string.IsNullOrWhiteSpace(_viewModel.ApiKey);
        RegenerateApiKeyButton.IsEnabled = apiEnabled && authEnabled;

        ApiKeyWarningTextBlock.Visibility = apiEnabled && !authEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GenerateApiKey()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes);
    }
}
