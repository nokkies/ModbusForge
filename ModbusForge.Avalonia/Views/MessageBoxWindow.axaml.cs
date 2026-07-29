using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class MessageBoxWindow : global::Avalonia.Controls.Window
    {
        public MessageBoxWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }
    }
}
