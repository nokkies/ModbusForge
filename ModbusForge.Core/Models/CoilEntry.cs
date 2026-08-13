using System.ComponentModel;

namespace ModbusForge.Models
{
    public class CoilEntry : INotifyPropertyChanged
    {
        private int _address;
        private bool _state;
        private bool _isReadError;
        private string? _readErrorMessage;

        public int Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } }
        }

        public bool State
        {
            get => _state;
            set { if (_state != value) { _state = value; OnPropertyChanged(nameof(State)); } }
        }

        public bool IsReadError
        {
            get => _isReadError;
            set { if (_isReadError != value) { _isReadError = value; OnPropertyChanged(nameof(IsReadError)); } }
        }

        public string? ReadErrorMessage
        {
            get => _readErrorMessage;
            set { if (_readErrorMessage != value) { _readErrorMessage = value; OnPropertyChanged(nameof(ReadErrorMessage)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
