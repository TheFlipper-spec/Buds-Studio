using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RealmeBudsPopup.Models;
using RealmeBudsPopup.Services;

namespace RealmeBudsPopup.Views
{
    public partial class BudsPopupWindow : Window
    {
        private const double StartTop = -200.0;
        private readonly DispatcherTimer _dismissTimer;
        private bool _isClosing;
        private BudsBatteryState? _currentState;

        public BudsPopupWindow(BudsBatteryState? initialState = null)
        {
            InitializeComponent();

            double duration = ThemeSettings.Current.DisplayDurationSeconds;
            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(duration)
            };
            _dismissTimer.Tick += OnDismissTimerTick;

            Loaded += OnWindowLoaded;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;

            ApplyThemeAndColors();

            if (initialState != null)
            {
                UpdateState(initialState);
            }
        }

        public void ApplyThemeAndColors()
        {
            var settings = ThemeSettings.Current;

            // Apply theme surfaces
            CardBorder.Background = settings.GetWindowBackground();
            CardBorder.BorderBrush = settings.GetWindowBorder();

            var textPrimary = settings.GetTextPrimary();
            var textSecondary = settings.GetTextSecondary();
            var trackBrush = settings.GetTrackBrush();
            var progressBrush = settings.GetProgressBrush();
            var budBrush = settings.GetBudBrush();

            // Text colors
            LeftPercentText.Foreground = textPrimary;
            CasePercentText.Foreground = textPrimary;
            RightPercentText.Foreground = textPrimary;
            OverEarPercentText.Foreground = textPrimary;

            LeftStatusText.Foreground = textSecondary;
            CaseStatusText.Foreground = textSecondary;
            RightStatusText.Foreground = textSecondary;
            OverEarStatusText.Foreground = textSecondary;

            // Gauge colors
            LeftGauge.TrackBrush = trackBrush;
            LeftGauge.ProgressBrush = progressBrush;

            CaseGauge.TrackBrush = trackBrush;
            CaseGauge.ProgressBrush = progressBrush;

            RightGauge.TrackBrush = trackBrush;
            RightGauge.ProgressBrush = progressBrush;

            OverEarGauge.TrackBrush = trackBrush;
            OverEarGauge.ProgressBrush = progressBrush;

            // Earbud & Case Silhouettes
            LeftBudStem.Fill = budBrush;
            LeftBudHead.Fill = budBrush;
            LeftBudTip.Fill = budBrush;

            CaseBody.Fill = budBrush;

            RightBudStem.Fill = budBrush;
            RightBudHead.Fill = budBrush;
            RightBudTip.Fill = budBrush;

            // Over-Ear Vector Elements
            OverEarArch.Stroke = budBrush;
            OverEarLeftCup.Fill = budBrush;
            OverEarRightCup.Fill = budBrush;

            // Tip outline matches background for clean cutout effect
            var cutoutBrush = settings.Theme == AppTheme.Light
                ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                : new SolidColorBrush(Color.FromRgb(18, 18, 22));

            LeftBudTip.Stroke = cutoutBrush;
            RightBudTip.Stroke = cutoutBrush;

            // Layout visibility based on model type
            if (settings.ModelType == HeadphoneModelType.OverEar)
            {
                TwsLayout.Visibility = Visibility.Collapsed;
                OverEarLayout.Visibility = Visibility.Visible;
            }
            else
            {
                TwsLayout.Visibility = Visibility.Visible;
                OverEarLayout.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateState(BudsBatteryState state)
        {
            _currentState = state;

            Dispatcher.Invoke(() =>
            {
                // Auto-detect Over-Ear or TWS Buds based on connected device name
                string name = state.DeviceName.ToLowerInvariant();
                if (name.Contains("edifier") || name.Contains("wh-") || name.Contains("max") || name.Contains("w800") || name.Contains("w820") || name.Contains("over-ear"))
                {
                    ThemeSettings.Current.ModelType = HeadphoneModelType.OverEar;
                }
                else
                {
                    ThemeSettings.Current.ModelType = HeadphoneModelType.InEar;
                }
                ApplyThemeAndColors();

                if (ThemeSettings.Current.ModelType == HeadphoneModelType.OverEar)
                {
                    // Single battery for Over-Ear
                    int? val = state.Left.Percent ?? state.Right.Percent ?? state.Case.Percent;
                    if (val.HasValue)
                    {
                        OverEarPercentText.Text = $"{val.Value}%";
                        OverEarStatusText.Text = (state.Left.IsCharging || state.Right.IsCharging) ? "Зарядка" : "Подключено";
                        OverEarGauge.IsAvailable = true;
                        AnimateGauge(OverEarGauge, val.Value);
                    }
                    else
                    {
                        OverEarPercentText.Text = "—";
                        OverEarStatusText.Text = "Подключено";
                        OverEarGauge.IsAvailable = false;
                    }
                }
                else
                {
                    // TWS 3 components: Left Bud
                    if (state.Left.IsAvailable && state.Left.Percent.HasValue)
                    {
                        int val = state.Left.Percent.Value;
                        LeftPercentText.Text = $"{val}%";
                        LeftStatusText.Text = state.Left.IsCharging ? "Зарядка" : "В ухе";
                        LeftGauge.IsAvailable = true;
                        AnimateGauge(LeftGauge, val);
                    }
                    else
                    {
                        LeftPercentText.Text = "—";
                        LeftStatusText.Text = "Отключен";
                        LeftGauge.IsAvailable = false;
                    }

                    // Case
                    if (state.Case.IsAvailable && state.Case.Percent.HasValue)
                    {
                        int val = state.Case.Percent.Value;
                        CasePercentText.Text = $"{val}%";
                        CaseStatusText.Text = state.Case.IsCharging ? "Зарядка" : "Кейс";
                        CaseGauge.IsAvailable = true;
                        AnimateGauge(CaseGauge, val);
                    }
                    else
                    {
                        CasePercentText.Text = "—";
                        CaseStatusText.Text = "Кейс";
                        CaseGauge.IsAvailable = false;
                        CaseGauge.Value = 0;
                    }

                    // Right Bud
                    if (state.Right.IsAvailable && state.Right.Percent.HasValue)
                    {
                        int val = state.Right.Percent.Value;
                        RightPercentText.Text = $"{val}%";
                        RightStatusText.Text = state.Right.IsCharging ? "Зарядка" : "В ухе";
                        RightGauge.IsAvailable = true;
                        AnimateGauge(RightGauge, val);
                    }
                    else
                    {
                        RightPercentText.Text = "—";
                        RightStatusText.Text = "Отключен";
                        RightGauge.IsAvailable = false;
                    }
                }
            });
        }

        private static void AnimateGauge(Controls.CircularGauge gauge, double targetValue)
        {
            var anim = new DoubleAnimation
            {
                From = 0.0,
                To = targetValue,
                Duration = TimeSpan.FromSeconds(0.75),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            gauge.BeginAnimation(Controls.CircularGauge.ValueProperty, anim);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - Width) / 2.0;
            Top = StartTop;
            Opacity = 0.0;

            PlaySlideInAnimation();
        }

        private void PlaySlideInAnimation()
        {
            double targetTop = ThemeSettings.Current.TargetTopY;

            var springEase = new BackEase
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.22
            };

            var topAnim = new DoubleAnimation
            {
                From = StartTop,
                To = targetTop,
                Duration = TimeSpan.FromSeconds(0.45),
                EasingFunction = springEase
            };

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.35),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(topAnim);
            storyboard.Children.Add(opacityAnim);

            Storyboard.SetTarget(topAnim, this);
            Storyboard.SetTargetProperty(topAnim, new PropertyPath(Window.TopProperty));

            Storyboard.SetTarget(opacityAnim, this);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(Window.OpacityProperty));

            storyboard.Completed += (s, e) =>
            {
                _dismissTimer.Start();
            };

            storyboard.Begin();
        }

        private void PlaySlideOutAndClose()
        {
            if (_isClosing)
                return;

            _isClosing = true;
            _dismissTimer.Stop();

            var ease = new CircleEase { EasingMode = EasingMode.EaseIn };

            var topAnim = new DoubleAnimation
            {
                From = Top,
                To = StartTop,
                Duration = TimeSpan.FromSeconds(0.32),
                EasingFunction = ease
            };

            var opacityAnim = new DoubleAnimation
            {
                From = Opacity,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.30),
                EasingFunction = ease
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(topAnim);
            storyboard.Children.Add(opacityAnim);

            Storyboard.SetTarget(topAnim, this);
            Storyboard.SetTargetProperty(topAnim, new PropertyPath(Window.TopProperty));

            Storyboard.SetTarget(opacityAnim, this);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(Window.OpacityProperty));

            storyboard.Completed += (s, e) =>
            {
                Close();
            };

            storyboard.Begin();
        }

        private void OnDismissTimerTick(object? sender, EventArgs e)
        {
            _dismissTimer.Stop();
            PlaySlideOutAndClose();
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _dismissTimer.Stop();
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _dismissTimer.Start();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySlideOutAndClose();
        }
    }
}
