using System;
using System.ComponentModel;
using System.Globalization;

namespace ModbusForge.Models
{
    public class CustomEntry : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _name = string.Empty;
        private int _address;
        private string _type = "uint"; // uint,int,real
        private string _value = "0";
        private bool _continuous = false;
        private int _periodMs = 1000;
        public DateTime LastWriteUtc { get; set; } = DateTime.MinValue;
        // Read monitoring support
        private bool _monitor = false;
        private int _readPeriodMs = 1000;
        public DateTime LastReadUtc { get; set; } = DateTime.MinValue;
        private string _area = "HoldingRegister"; // HoldingRegister, Coil, InputRegister, DiscreteInput
        // Trend selection support
        private bool _trend = false;

        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } } }
        public int Address { get => _address; set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } } }
        public string Type { get => _type; set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); OnPropertyChanged(nameof(Value)); } } }
        public string Value { get => _value; set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } } }
        public bool Continuous { get => _continuous; set { if (_continuous != value) { _continuous = value; OnPropertyChanged(nameof(Continuous)); } } }
        public int PeriodMs { get => _periodMs; set { if (_periodMs != value) { _periodMs = value; OnPropertyChanged(nameof(PeriodMs)); } } }
        // Per-row continuous read
        public bool Monitor { get => _monitor; set { if (_monitor != value) { _monitor = value; OnPropertyChanged(nameof(Monitor)); } } }
        public int ReadPeriodMs { get => _readPeriodMs; set { if (_readPeriodMs != value) { _readPeriodMs = value; OnPropertyChanged(nameof(ReadPeriodMs)); } } }
        public string Area { get => _area; set { if (_area != value) { _area = value; OnPropertyChanged(nameof(Area)); } } }
        public bool Trend { get => _trend; set { if (_trend != value) { _trend = value; OnPropertyChanged(nameof(Trend)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // IDataErrorInfo validation
        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Name) && string.IsNullOrWhiteSpace(_name))
                    return "Name is required.";

                if (columnName == nameof(Address) && _address < 0)
                    return "Address cannot be negative.";

                if (columnName == nameof(PeriodMs) && _periodMs <= 0)
                    return "Period must be greater than 0 ms.";

                if (columnName == nameof(ReadPeriodMs) && _readPeriodMs <= 0)
                    return "Read period must be greater than 0 ms.";

                if (columnName == nameof(Value))
                {
                    if (string.IsNullOrWhiteSpace(_value))
                        return "Value is required.";

                    var type = (_type ?? "uint").ToLowerInvariant();
                    switch (type)
                    {
                        case "uint":
                            if (!ushort.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                return "Value must be an unsigned integer (0-65535).";
                            break;
                        case "int":
                            if (!short.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                return "Value must be a signed integer (-32768 to 32767).";
                            break;
                        case "real":
                            if (!float.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                                return "Value must be a number.";
                            break;
                    }
                }

                return string.Empty;
            }
        }
    }
}
