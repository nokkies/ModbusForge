using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ModbusForge.Models;

namespace ModbusForge.Views
{
    public partial class TestDialog : Window
    {
        public PlcArea SelectedArea { get; private set; }
        public int SelectedAddress { get; private set; }

        public TestDialog(PlcArea area, int address)
        {
            InitializeComponent();
            
            // Load the ComboBox with enum values
            TestComboBox.ItemsSource = Enum.GetValues<PlcArea>().ToList();
            
            // Show what we received
            DebugText.Text = $"Received: Area={area}, Address={address}";
            
            // Set the values directly
            TestComboBox.SelectedItem = area;
            TestTextBox.Text = address.ToString();
            
            SelectedArea = area;
            SelectedAddress = address;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedArea = (PlcArea)TestComboBox.SelectedItem;
            SelectedAddress = int.Parse(TestTextBox.Text);
            DialogResult = true;
            Close();
        }
    }
}
