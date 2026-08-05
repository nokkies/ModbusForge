using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// Attached behavior for visual node borders so the selected state can be
    /// applied directly without relying on style-class bindings.
    /// </summary>
    public class VisualNodeBehavior
    {
        static VisualNodeBehavior()
        {
            IsSelectedProperty.Changed.AddClassHandler<Border>(OnIsSelectedChanged);
        }

        public static readonly AttachedProperty<bool> IsSelectedProperty =
            AvaloniaProperty.RegisterAttached<VisualNodeBehavior, Border, bool>(
                "IsSelected",
                defaultValue: false,
                defaultBindingMode: global::Avalonia.Data.BindingMode.OneWay);

        public static bool GetIsSelected(Border border) => border.GetValue(IsSelectedProperty);

        public static void SetIsSelected(Border border, bool value) => border.SetValue(IsSelectedProperty, value);

        private static void OnIsSelectedChanged(Border border, AvaloniaPropertyChangedEventArgs e)
        {
            var isSelected = e.GetNewValue<bool>();
            border.BorderBrush = isSelected ? new SolidColorBrush(Color.Parse("#1976D2")) : Brushes.Black;
            border.Background = isSelected ? new SolidColorBrush(Color.Parse("#BBDEFB")) : Brushes.LightBlue;
            border.BorderThickness = isSelected ? new Thickness(3) : new Thickness(2);
        }
    }
}
