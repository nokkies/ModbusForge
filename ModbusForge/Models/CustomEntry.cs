using System;
using System.ComponentModel;

namespace ModbusForge.Models
{
    public class CustomEntry : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _address;
        private string _type = "uint"; // uint,int,real
        private string _value = "0";
        private bool _continuous = false;
        private int _periodMs = 1000;
        internal DateTime _lastWriteUtc = DateTime.MinValue;
        // Read monitoring support
        private bool _monitor = false;
        private int _readPeriodMs = 1000;
        internal DateTime _lastReadUtc = DateTime.MinValue;
        private string _area = "HoldingRegister"; // HoldingRegister, Coil, InputRegister, DiscreteInput
        // Trend selection support
        private bool _trend = false;

        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } } }
        public int Address { get => _address; set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } } }
        public string Type { get => _type; set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } } }
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
    }
}
