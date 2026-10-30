using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MacExtend.Themes;

namespace MacExtend.Views
{
    /// <summary>
    /// "Mở điều khiển" could not start a session: why, and at most one action that fixes it ("Thiết lập lại"). The
    /// action reports what it is doing in the window; once it succeeds the dialog closes with true.
    /// </summary>
    internal partial class DirectPromptWindow : Window
    {
        private readonly Func<Action<string>, Task<bool>> _action;
        private bool _running;
        private bool _closed;

        /// <param name="action">Gets a callback for its status line; true when the problem is fixed. null: only "Đóng".</param>
        public DirectPromptWindow(string title, string detail, string actionText, Func<Action<string>, Task<bool>> action)
        {
            InitializeComponent();
            Heading.Text = title ?? "";
            Detail.Text = detail ?? "";
            _action = action;
            if (action != null && !string.IsNullOrEmpty(actionText))
            {
                ActionButton.Content = actionText;
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.IsDefault = true;
            }
            else
            {
                CloseButton.IsDefault = true;
            }

            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
            Spinner.IsVisibleChanged += (_, __) => SpinnerTurn.BeginAnimation(RotateTransform.AngleProperty, Spinner.IsVisible ? spin : null);
            SourceInitialized += (_, __) => ThemeManager.ApplyWindowFrame(this);
            Closed += (_, __) => _closed = true;
        }

        private async void OnAction(object sender, RoutedEventArgs e)
        {
            if (_running || _action == null) return;
            _running = true;
            ActionButton.IsEnabled = false;
            Spinner.Visibility = Visibility.Visible;
            bool fixedIt;
            try
            {
                fixedIt = await _action(Say);
            }
            finally
            {
                _running = false;
                // closed meanwhile (the UAC prompt stays up after "Đóng"): the action still finished
                if (!_closed)
                {
                    ActionButton.IsEnabled = true;
                    Spinner.Visibility = Visibility.Collapsed;
                }
            }
            if (fixedIt && !_closed) DialogResult = true;
        }

        private void Say(string text)
        {
            if (_closed) return;
            Status.Text = text ?? "";
            StatusLine.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
