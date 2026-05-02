using System.ComponentModel;

namespace ModbusForge.Models
{
    public class RegisterEntry : INotifyPropertyChanged
    {
        private int _address;
        private ushort _value;
        private string _type = "uint";
        private string _valueText = "0"; // used for editing/display to avoid WPF ConvertBack to ushort

        public int Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } }
        }

        public ushort Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                    // keep ValueText in sync for display
                    var s = _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (_valueText != s)
                    {
                        _valueText = s;
                        OnPropertyChanged(nameof(ValueText));
                    }
                }
            }
        }

        public string Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } }
        }

        // String representation shown/edited in the grid to avoid ConvertBack to ushort for 'real'/'string'
        public string ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText != value)
                {
                    _valueText = value;
                    OnPropertyChanged(nameof(ValueText));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
