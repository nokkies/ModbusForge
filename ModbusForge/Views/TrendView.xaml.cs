using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.ViewModels;
using System.ComponentModel;

namespace ModbusForge.Views
{
    public partial class TrendView : UserControl
    {
        public TrendView()
        {
            InitializeComponent();
            // Avoid resolving services during design-time to keep the XAML designer happy
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = App.ServiceProvider.GetRequiredService<TrendViewModel>();
            }
        }
    }
}
