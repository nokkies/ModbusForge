using Microsoft.Extensions.DependencyInjection;
using ModbusForge.ViewModels;
using System.Windows.Controls;

namespace ModbusForge.Views
{
    public partial class DecodeView : UserControl
    {
        public DecodeView()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<DecodeViewModel>();
        }
    }
}