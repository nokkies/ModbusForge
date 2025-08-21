using System.ComponentModel;

namespace ModbusForge.Models
{
    public class RegisterEntry : INotifyPropertyChanged
    {
        private int _address;
        private ushort _value;
        private string _type = "uint";

        public int Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } }
        }

        public ushort Value
        {
            get => _value;
            set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } }
        }

        public string Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
