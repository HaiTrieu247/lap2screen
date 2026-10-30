using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MacExtend.Themes;
using MacExtend.ViewModels;

namespace MacExtend.Views
{
    /// <summary>
    /// "Thiết lập điều khiển trực tiếp": the one-time steps of <see cref="CoreDeviceSetupViewModel"/>, read again each time
    /// the window opens. Closing it stops a runtime download; a step already running finishes in the background.
    /// </summary>
    internal partial class CoreDeviceSetupWindow : Window
    {
        private readonly CoreDeviceSetupViewModel _vm;

        public CoreDeviceSetupWindow(CoreDeviceSetupViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            InitializeComponent();
            DataContext = vm;
            // fits laptops at 150 % scaling, where 1080p leaves about 670 px under the taskbar; the steps scroll inside
            MaxHeight = SystemParameters.WorkArea.Height - 24;

            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
            Spinner.IsVisibleChanged += (_, __) => SpinnerTurn.BeginAnimation(RotateTransform.AngleProperty, Spinner.IsVisible ? spin : null);

            SourceInitialized += (_, __) => ThemeManager.ApplyWindowFrame(this);
            Loaded += OnLoaded;
            Closed += (_, __) => _vm.CancelWork();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            // not awaited: the check never leaves the window busy, and nothing in it may reach the dispatcher's crash handler
            _ = _vm.RefreshAsync();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
