using System;
using System.Security.Cryptography;
using System.Windows;
using ModbusForge.Services;

namespace ModbusForge;

public partial class PreferencesWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;

    public PreferencesWindow(ISettingsService settingsService, IDialogService? dialogService = null)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _dialogService = dialogService ?? new NullDialogService();
        LoadSettings();
    }

    private void LoadSettings()
    {
        AutoReconnectCheckBox.IsChecked = _settingsService.AutoReconnect;
        ReconnectIntervalTextBox.Text = _settingsService.AutoReconnectIntervalMs.ToString();
        ShowDiagnosticsOnErrorCheckBox.IsChecked = _settingsService.ShowConnectionDiagnosticsOnError;
        EnableConsoleLoggingCheckBox.IsChecked = _settingsService.EnableConsoleLogging;
        MaxConsoleMessagesTextBox.Text = _settingsService.MaxConsoleMessages.ToString();
        MaxConcurrentTrendRequestsTextBox.Text = _settingsService.MaxConcurrentTrendRequests.ToString();
        ConfirmOnExitCheckBox.IsChecked = _settingsService.ConfirmOnExit;
        EnableApiCheckBox.IsChecked = _settingsService.EnableApi;
        ApiPortTextBox.Text = _settingsService.ApiPort.ToString();
        EnableApiDocumentationCheckBox.IsChecked = _settingsService.EnableApiDocumentation;
        EnableApiAuthenticationCheckBox.IsChecked = _settingsService.EnableApiAuthentication;
        ApiKeyTextBox.Text = _settingsService.ApiKey;
        UpdateApiKeyVisibility();
    }

    private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _settingsService.AutoReconnect = AutoReconnectCheckBox.IsChecked ?? false;
        
        if (int.TryParse(ReconnectIntervalTextBox.Text, out int reconnectInterval))
        {
            _settingsService.AutoReconnectIntervalMs = reconnectInterval;
        }

        _settingsService.ShowConnectionDiagnosticsOnError = ShowDiagnosticsOnErrorCheckBox.IsChecked ?? true;
        _settingsService.EnableConsoleLogging = EnableConsoleLoggingCheckBox.IsChecked ?? true;

        if (int.TryParse(MaxConsoleMessagesTextBox.Text, out int maxMessages))
        {
            _settingsService.MaxConsoleMessages = maxMessages;
        }

        if (int.TryParse(MaxConcurrentTrendRequestsTextBox.Text, out int maxConcurrent))
        {
            _settingsService.MaxConcurrentTrendRequests = Math.Max(1, maxConcurrent); // Ensure at least 1
        }

        _settingsService.ConfirmOnExit = ConfirmOnExitCheckBox.IsChecked ?? false;

        _settingsService.EnableApi = EnableApiCheckBox.IsChecked ?? false;
        if (int.TryParse(ApiPortTextBox.Text, out int apiPort))
        {
            _settingsService.ApiPort = apiPort;
        }

        _settingsService.EnableApiDocumentation = EnableApiDocumentationCheckBox.IsChecked ?? false;
        _settingsService.EnableApiAuthentication = EnableApiAuthenticationCheckBox.IsChecked ?? false;

        if (EnableApiAuthenticationCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(ApiKeyTextBox.Text))
        {
            ApiKeyTextBox.Text = GenerateApiKey();
        }

        _settingsService.ApiKey = ApiKeyTextBox.Text.Trim();

        if (_settingsService.Save())
        {
            DialogResult = true;
            Close();
        }
        else
        {
            _dialogService.Show(
                "Failed to save settings. Please check your permissions or disk space.",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void EnableApiCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility();
    }

    private void EnableApiAuthenticationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility();
        if (EnableApiAuthenticationCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(ApiKeyTextBox.Text))
        {
            ApiKeyTextBox.Text = GenerateApiKey();
        }
    }

    private void CopyApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyTextBox.Text))
        {
            Clipboard.SetText(ApiKeyTextBox.Text);
        }
    }

    private void RegenerateApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyTextBox.Text = GenerateApiKey();
    }

    private void UpdateApiKeyVisibility()
    {
        bool apiEnabled = EnableApiCheckBox.IsChecked == true;
        bool authEnabled = EnableApiAuthenticationCheckBox.IsChecked == true;

        EnableApiAuthenticationCheckBox.IsEnabled = apiEnabled;
        ApiKeyTextBox.IsEnabled = apiEnabled;
        CopyApiKeyButton.IsEnabled = apiEnabled && !string.IsNullOrWhiteSpace(ApiKeyTextBox.Text);
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
