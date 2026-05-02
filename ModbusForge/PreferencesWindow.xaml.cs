using MahApps.Metro.Controls;
using ModbusForge.Services;

namespace ModbusForge;

public partial class PreferencesWindow : MetroWindow
{
    private readonly ISettingsService _settingsService;

    public PreferencesWindow(ISettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        AutoReconnectCheckBox.IsChecked = _settingsService.AutoReconnect;
        ReconnectIntervalTextBox.Text = _settingsService.AutoReconnectIntervalMs.ToString();
        ShowDiagnosticsOnErrorCheckBox.IsChecked = _settingsService.ShowConnectionDiagnosticsOnError;
        EnableConsoleLoggingCheckBox.IsChecked = _settingsService.EnableConsoleLogging;
        MaxConsoleMessagesTextBox.Text = _settingsService.MaxConsoleMessages.ToString();
        ConfirmOnExitCheckBox.IsChecked = _settingsService.ConfirmOnExit;
        EnableApiCheckBox.IsChecked = _settingsService.EnableApi;
        ApiPortTextBox.Text = _settingsService.ApiPort.ToString();
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

        _settingsService.ConfirmOnExit = ConfirmOnExitCheckBox.IsChecked ?? false;

        _settingsService.EnableApi = EnableApiCheckBox.IsChecked ?? false;
        if (int.TryParse(ApiPortTextBox.Text, out int apiPort))
        {
            _settingsService.ApiPort = apiPort;
        }

        _settingsService.Save();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
