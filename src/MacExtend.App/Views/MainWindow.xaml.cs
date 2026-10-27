using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MacExtend.Themes;
using MacExtend.ViewModels;

namespace MacExtend.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly DispatcherTimer _statusRefresh;
        private bool _allowClose;

        internal MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // fits laptops at 150 % scaling, where 1080p leaves about 670 px under the taskbar
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);

            _statusRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _statusRefresh.Tick += async (_, __) => await _vm.RefreshSystemStatusAsync();

            SourceInitialized += (_, __) => ThemeManager.ApplyWindowFrame(this);
            IsVisibleChanged += async (_, __) =>
            {
                if (IsVisible)
                {
                    _statusRefresh.Start();
                    await _vm.RefreshSystemStatusAsync();
                }
                else
                {
                    _statusRefresh.Stop();
                }
            };
        }

        /// <summary>The window went to the tray (close button). The app keeps running.</summary>
        internal event Action HiddenToTray;

        internal void ShowAndActivate()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            // bring to front even when another app has focus
            Topmost = true;
            Topmost = false;
            Focus();
        }

        internal void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideToTray();
            }
            base.OnClosing(e);
        }

        private void HideToTray()
        {
            Hide();
            HiddenToTray?.Invoke();
        }

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnClose(object sender, RoutedEventArgs e) => HideToTray();
    }
}
