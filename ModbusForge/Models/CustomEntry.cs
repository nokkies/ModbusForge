using System;
using System.ComponentModel;

namespace ModbusForge.Models
{
    public class CustomEntry : INotifyPropertyChanged
    {
        private int _address;
        private string _type = "uint"; // uint,int,real
        private string _value = "0";
        private bool _continuous = false;
        private int _periodMs = 1000;
        internal DateTime _lastWriteUtc = DateTime.MinValue;

        public int Address { get => _address; set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } } }
        public string Type { get => _type; set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } } }
        public string Value { get => _value; set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } } }
        public bool Continuous { get => _continuous; set { if (_continuous != value) { _continuous = value; OnPropertyChanged(nameof(Continuous)); } } }
        public int PeriodMs { get => _periodMs; set { if (_periodMs != value) { _periodMs = value; OnPropertyChanged(nameof(PeriodMs)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
