using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MacExtend.Core;

namespace MacExtend.Views
{
    public partial class DisplayStage : UserControl
    {
        public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
            nameof(Phase), typeof(ReceiverPhase), typeof(DisplayStage), new PropertyMetadata(ReceiverPhase.Stopped, Changed));

        public static readonly DependencyProperty ClientNameProperty = DependencyProperty.Register(
            nameof(ClientName), typeof(string), typeof(DisplayStage), new PropertyMetadata(null, Changed));

        public static readonly DependencyProperty DeviceNameProperty = DependencyProperty.Register(
            nameof(DeviceName), typeof(string), typeof(DisplayStage), new PropertyMetadata("", Changed));

        public static readonly DependencyProperty ModeTextProperty = DependencyProperty.Register(
            nameof(ModeText), typeof(string), typeof(DisplayStage), new PropertyMetadata("", Changed));

        private ReceiverPhase? _shownPhase;
        private bool _breathing;

        public DisplayStage()
        {
            InitializeComponent();
            Loaded += (_, __) => Apply();
            // the window spends most of the day hidden in the tray; an endless animation there costs battery
            IsVisibleChanged += (_, __) => UpdateBreathing();
        }

        public ReceiverPhase Phase
        {
            get => (ReceiverPhase)GetValue(PhaseProperty);
            set => SetValue(PhaseProperty, value);
        }

        public string ClientName
        {
            get => (string)GetValue(ClientNameProperty);
            set => SetValue(ClientNameProperty, value);
        }

        public string DeviceName
        {
            get => (string)GetValue(DeviceNameProperty);
            set => SetValue(DeviceNameProperty, value);
        }

        public string ModeText
        {
            get => (string)GetValue(ModeTextProperty);
            set => SetValue(ModeTextProperty, value);
        }

        private static bool MotionAllowed => SystemParameters.ClientAreaAnimation;

        /// <summary>The one ambient motion: a slow breath on the waiting outline, only while someone can see it.</summary>
        private void UpdateBreathing()
        {
            var breathe = IsLoaded && IsVisible && Phase == ReceiverPhase.Ready && MotionAllowed;
            if (breathe == _breathing) return;
            _breathing = breathe;
            if (!breathe)
            {
                SecondOutline.BeginAnimation(OpacityProperty, null);
                SecondOutline.Opacity = Phase == ReceiverPhase.Connected ? 0 : 1;
                return;
            }
            SecondOutline.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromSeconds(1.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }

        private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DisplayStage)d).Apply();

        private void Apply()
        {
            if (!IsLoaded) return;
            DeviceLabel.Text = DeviceName;
            ModeLabel.Text = ModeText;

            var connected = Phase == ReceiverPhase.Connected;
            MacLabel.Text = connected ? ClientName : "Mac của bạn";
            MacDisplay.Opacity = connected ? 1.0 : 0.38;
            DeviceLabel.SetResourceReference(TextBlock.ForegroundProperty, connected ? "Accent.Text" : "Stage.Text");
            ModeLabel.SetResourceReference(TextBlock.ForegroundProperty, connected ? "Accent.Text" : "Stage.TextDim");
            ModeLabel.Opacity = connected ? 0.8 : 1.0;

            switch (Phase)
            {
                case ReceiverPhase.Ready:
                    Caption.Text = "Đang chờ Mac chọn màn hình này";
                    SecondOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Status.Ready");
                    break;
                case ReceiverPhase.Connected:
                    Caption.Text = "Màn hình phụ của " + ClientName;
                    break;
                case ReceiverPhase.Failed:
                    Caption.Text = "Bộ nhận đã dừng vì lỗi";
                    SecondOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Status.Error");
                    break;
                case ReceiverPhase.Starting:
                    Caption.Text = "Đang khởi động bộ nhận";
                    SecondOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Stage.TextDim");
                    break;
                default:
                    Caption.Text = "Bộ nhận đang tắt";
                    SecondOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Stage.TextDim");
                    break;
            }

            if (_shownPhase == Phase) return;
            var previous = _shownPhase;
            _shownPhase = Phase;

            _breathing = false;
            SecondOutline.BeginAnimation(OpacityProperty, null);
            SecondOutline.Opacity = connected ? 0 : 1;
            UpdateBreathing();

            var fillTo = connected ? 1.0 : 0.0;
            if (MotionAllowed && previous != null)
            {
                SecondFill.BeginAnimation(OpacityProperty, new DoubleAnimation(fillTo, TimeSpan.FromMilliseconds(320))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
            else
            {
                SecondFill.BeginAnimation(OpacityProperty, null);
                SecondFill.Opacity = fillTo;
            }
        }
    }
}
