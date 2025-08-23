using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.ViewModels;

namespace ModbusForge.Views
{
    public partial class DecodeView : UserControl
    {
        public DecodeView()
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = App.ServiceProvider.GetRequiredService<DecodeViewModel>();
            }
        }
    }
}
