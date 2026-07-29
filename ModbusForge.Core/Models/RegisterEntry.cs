using System.ComponentModel;
using System.Globalization;

namespace ModbusForge.Models
{
    public class RegisterEntry : INotifyPropertyChanged, IDataErrorInfo
    {
        private int _address;
        private ushort _value;
        private string _type = "uint";
        private bool _swapBytes;
        private bool _swapWords;
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
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged(nameof(Type));
                    OnPropertyChanged(nameof(ValueText)); // re-validate value against new type
                }
            }
        }

        public bool SwapBytes
        {
            get => _swapBytes;
            set { if (_swapBytes != value) { _swapBytes = value; OnPropertyChanged(nameof(SwapBytes)); } }
        }

        public bool SwapWords
        {
            get => _swapWords;
            set { if (_swapWords != value) { _swapWords = value; OnPropertyChanged(nameof(SwapWords)); } }
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

        // IDataErrorInfo validation
        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Address) && Address < 0)
                    return "Address cannot be negative.";

                if (columnName == nameof(ValueText))
                {
                    if (string.IsNullOrWhiteSpace(_valueText))
                        return "Value is required.";

                    var type = (_type ?? "uint").ToLowerInvariant();
                    switch (type)
                    {
                        case "uint":
                            if (!ushort.TryParse(_valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                return "Value must be an unsigned integer (0-65535).";
                            break;
                        case "int":
                            if (!short.TryParse(_valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                return "Value must be a signed integer (-32768 to 32767).";
                            break;
                        case "real":
                            if (!float.TryParse(_valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                                return "Value must be a number.";
                            break;
                    }
                }

                return string.Empty;
            }
        }
    }
}
