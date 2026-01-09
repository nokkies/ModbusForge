using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge;

public partial class ConnectionManagerWindow : MetroWindow, INotifyPropertyChanged
{
    private readonly IConnectionManager _connectionManager;
    private ConnectionProfile? _selectedProfile;

    public event PropertyChangedEventHandler? PropertyChanged;

    public System.Collections.ObjectModel.ObservableCollection<ConnectionProfile> Profiles 
        => _connectionManager.Profiles;

    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedProfile)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelection)));
        }
    }

    public bool HasSelection => SelectedProfile != null;

    public ConnectionManagerWindow(IConnectionManager connectionManager)
    {
        InitializeComponent();
        _connectionManager = connectionManager;
        DataContext = this;

        if (Profiles.Count > 0)
        {
            SelectedProfile = _connectionManager.ActiveProfile ?? Profiles[0];
        }
    }

    private void ConnectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is ConnectionProfile profile)
        {
            SelectedProfile = profile;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var newProfile = new ConnectionProfile($"Connection {Profiles.Count + 1}", "127.0.0.1", 502, 1);
        _connectionManager.AddProfile(newProfile);
        SelectedProfile = newProfile;
        ConnectionsList.SelectedItem = newProfile;
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null) return;

        if (Profiles.Count <= 1)
        {
            MessageBox.Show("Cannot remove the last connection profile.", "Remove Profile", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"Remove connection '{SelectedProfile.Name}'?", "Confirm Remove",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var toRemove = SelectedProfile;
            SelectedProfile = Profiles.Count > 1 ? Profiles[0] : null;
            _connectionManager.RemoveProfile(toRemove);
        }
    }

    private void CloneButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null) return;

        var cloned = SelectedProfile.Clone();
        _connectionManager.AddProfile(cloned);
        SelectedProfile = cloned;
        ConnectionsList.SelectedItem = cloned;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null) return;

        ConnectButton.IsEnabled = false;
        try
        {
            var success = await _connectionManager.ConnectProfileAsync(SelectedProfile);
            if (!success)
            {
                MessageBox.Show($"Failed to connect to {SelectedProfile.IpAddress}:{SelectedProfile.Port}",
                    "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null) return;

        await _connectionManager.DisconnectProfileAsync(SelectedProfile);
    }

    private void SetActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null) return;

        _connectionManager.SetActiveProfile(SelectedProfile);
        MessageBox.Show($"'{SelectedProfile.Name}' is now the active connection.", "Active Connection",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _connectionManager.SaveProfiles();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
