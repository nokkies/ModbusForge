using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace ModbusForge.Behaviors
{
    /// <summary>
    /// Attached behavior that keeps a ListBox scrolled to the last item when its ItemsSource changes.
    /// </summary>
    public static class ListBoxAutoScrollBehavior
    {
        private static readonly Dictionary<ListBox, NotifyCollectionChangedEventHandler> _subscriptions = new();

        public static readonly DependencyProperty AutoScrollToBottomProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollToBottom",
                typeof(bool),
                typeof(ListBoxAutoScrollBehavior),
                new PropertyMetadata(false, OnAutoScrollChanged));

        public static bool GetAutoScrollToBottom(DependencyObject obj) => (bool)obj.GetValue(AutoScrollToBottomProperty);
        public static void SetAutoScrollToBottom(DependencyObject obj, bool value) => obj.SetValue(AutoScrollToBottomProperty, value);

        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox) return;

            if ((bool)e.NewValue)
            {
                listBox.Loaded += OnListBoxLoaded;
                listBox.Unloaded += OnListBoxUnloaded;
                if (listBox.IsLoaded)
                    Subscribe(listBox);
            }
            else
            {
                listBox.Loaded -= OnListBoxLoaded;
                listBox.Unloaded -= OnListBoxUnloaded;
                Unsubscribe(listBox);
            }
        }

        private static void OnListBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListBox listBox) Subscribe(listBox);
        }

        private static void OnListBoxUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListBox listBox) Unsubscribe(listBox);
        }

        private static void Subscribe(ListBox listBox)
        {
            Unsubscribe(listBox);

            if (listBox.Items is not INotifyCollectionChanged source) return;

            NotifyCollectionChangedEventHandler handler = (s, args) => ScrollToBottom(listBox);
            lock (_subscriptions)
            {
                _subscriptions[listBox] = handler;
            }
            source.CollectionChanged += handler;
        }

        private static void Unsubscribe(ListBox listBox)
        {
            NotifyCollectionChangedEventHandler? handler;
            lock (_subscriptions)
            {
                if (!_subscriptions.TryGetValue(listBox, out handler)) return;
                _subscriptions.Remove(listBox);
            }

            if (listBox.Items is INotifyCollectionChanged source)
            {
                source.CollectionChanged -= handler;
            }
        }

        private static void ScrollToBottom(ListBox listBox)
        {
            if (listBox.Items.Count == 0) return;

            var lastItem = listBox.Items[listBox.Items.Count - 1];
            listBox.Dispatcher.BeginInvoke(new Action(() => listBox.ScrollIntoView(lastItem)), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
