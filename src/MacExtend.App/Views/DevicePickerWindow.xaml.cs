using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacExtend.Core.CoreDevice;
using MacExtend.Themes;

namespace MacExtend.Views
{
    /// <summary>"Mở điều khiển" found several devices: the user picks one. Cable devices come first; the first is chosen.</summary>
    internal partial class DevicePickerWindow : Window
    {
        public DevicePickerWindow(IReadOnlyList<DeviceChoice> choices)
        {
            if (choices == null || choices.Count == 0) throw new ArgumentException("No device to choose from.", nameof(choices));
            InitializeComponent();
            Devices.ItemsSource = choices;
            Devices.SelectedIndex = 0;
            SourceInitialized += (_, __) => ThemeManager.ApplyWindowFrame(this);
            // the arrows move between the devices at once, Enter takes the chosen one
            Loaded += (_, __) => (Devices.ItemContainerGenerator.ContainerFromIndex(Devices.SelectedIndex) as ListBoxItem)?.Focus();
        }

        /// <summary>The device picked; null until the dialog was confirmed.</summary>
        public DeviceChoice Chosen { get; private set; }

        private void OnChoose(object sender, RoutedEventArgs e)
        {
            if (!(Devices.SelectedItem is DeviceChoice choice)) return;
            Chosen = choice;
            DialogResult = true;
        }

        /// <summary>A double click on a device takes it; one on the list's empty space or its scroll bar does nothing.</summary>
        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || !(e.OriginalSource is DependencyObject source)) return;
            if (ItemsControl.ContainerFromElement(Devices, source) is ListBoxItem) OnChoose(sender, e);
        }
    }
}
