using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RealmeBudsPopup.Models;
using RealmeBudsPopup.Services;

namespace RealmeBudsPopup.Views
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041.0")]
    public partial class SettingsWindow : Window
    {
        private bool _isInitializing = true;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
            _isInitializing = false;
            UpdateLivePreview();
        }

        private void LoadCurrentSettings()
        {
            var s = ThemeSettings.Current;

            ThemeLightRadio.IsChecked = s.Theme == AppTheme.Light;
            ThemeDarkRadio.IsChecked = s.Theme == AppTheme.Dark;

            ShowInTrayCheck.IsChecked = s.ShowBatteryInTray;
            AutorunCheck.IsChecked = StartupManager.IsAutoStartEnabled();

            DurationSlider.Value = s.DisplayDurationSeconds;
            DurationLabel.Text = $"{s.DisplayDurationSeconds:F1} с";

            TopYSlider.Value = s.TargetTopY;
            TopYLabel.Text = $"{s.TargetTopY:F0} px";

            // Apply layout visibility on Hero card and Preview based on auto-detected model type
            if (s.ModelType == HeadphoneModelType.OverEar)
            {
                OverviewTwsLayout.Visibility = Visibility.Collapsed;
                OverviewOverEarLayout.Visibility = Visibility.Visible;
                PreviewTwsLayout.Visibility = Visibility.Collapsed;
                PreviewOverEarLayout.Visibility = Visibility.Visible;
            }
            else
            {
                OverviewTwsLayout.Visibility = Visibility.Visible;
                OverviewOverEarLayout.Visibility = Visibility.Collapsed;
                PreviewTwsLayout.Visibility = Visibility.Visible;
                PreviewOverEarLayout.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateLiveBatteryState(BudsBatteryState? state)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (state == null)
                    {
                        HeaderStatusText.Text = "Наушники не подключены (Поиск...)";
                        HeaderStatusDot.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));

                        OverviewLeftPercent.Text = "—";
                        OverviewLeftStatus.Text = "Отключен";
                        OverviewLeftGauge.IsAvailable = false;

                        OverviewCasePercent.Text = "—";
                        OverviewCaseStatus.Text = "Кейс";
                        OverviewCaseGauge.IsAvailable = false;

                        OverviewRightPercent.Text = "—";
                        OverviewRightStatus.Text = "Отключен";
                        OverviewRightGauge.IsAvailable = false;

                        OverviewOverEarPercent.Text = "—";
                        OverviewOverEarStatus.Text = "Отключено";
                        OverviewOverEarGauge.IsAvailable = false;
                        return;
                    }

                    App.AutoDetectHeadphoneModel(state.DeviceName);
                    UpdateLivePreview();

                    HeaderStatusText.Text = $"{state.DeviceName} — Подключено";
                    HeaderStatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));

                    var s = ThemeSettings.Current;

                    if (s.ModelType == HeadphoneModelType.OverEar)
                    {
                        OverviewTwsLayout.Visibility = Visibility.Collapsed;
                        OverviewOverEarLayout.Visibility = Visibility.Visible;

                        int? mainBattery = state.Left.Percent ?? state.Right.Percent ?? state.Case.Percent;
                        if (mainBattery.HasValue)
                        {
                            OverviewOverEarPercent.Text = $"{mainBattery.Value}%";
                            OverviewOverEarStatus.Text = (state.Left.IsCharging || state.Right.IsCharging) ? "Зарядка" : "Подключено";
                            OverviewOverEarGauge.IsAvailable = true;
                            AnimateGauge(OverviewOverEarGauge, mainBattery.Value);
                        }
                    }
                    else
                    {
                        OverviewTwsLayout.Visibility = Visibility.Visible;
                        OverviewOverEarLayout.Visibility = Visibility.Collapsed;

                        // 1. Left Bud
                        if (state.Left.IsAvailable && state.Left.Percent.HasValue)
                        {
                            int val = state.Left.Percent.Value;
                            OverviewLeftPercent.Text = $"{val}%";
                            OverviewLeftStatus.Text = state.Left.IsCharging ? "Зарядка" : "В ухе";
                            OverviewLeftGauge.IsAvailable = true;
                            AnimateGauge(OverviewLeftGauge, val);
                        }
                        else
                        {
                            OverviewLeftPercent.Text = "—";
                            OverviewLeftStatus.Text = "Отключен";
                            OverviewLeftGauge.IsAvailable = false;
                        }

                        // 2. Case
                        if (state.Case.IsAvailable && state.Case.Percent.HasValue && state.Case.Percent.Value > 0)
                        {
                            int val = state.Case.Percent.Value;
                            OverviewCasePercent.Text = $"{val}%";
                            OverviewCaseStatus.Text = state.Case.IsCharging ? "Зарядка" : "Кейс открыт";
                            OverviewCaseGauge.IsAvailable = true;
                            AnimateGauge(OverviewCaseGauge, val);
                        }
                        else
                        {
                            OverviewCasePercent.Text = "—";
                            OverviewCaseStatus.Text = "Кейс (в ушах)";
                            OverviewCaseGauge.IsAvailable = false;
                            OverviewCaseGauge.Value = 0;
                        }

                        // 3. Right Bud
                        if (state.Right.IsAvailable && state.Right.Percent.HasValue)
                        {
                            int val = state.Right.Percent.Value;
                            OverviewRightPercent.Text = $"{val}%";
                            OverviewRightStatus.Text = state.Right.IsCharging ? "Зарядка" : "В ухе";
                            OverviewRightGauge.IsAvailable = true;
                            AnimateGauge(OverviewRightGauge, val);
                        }
                        else
                        {
                            OverviewRightPercent.Text = "—";
                            OverviewRightStatus.Text = "Отключен";
                            OverviewRightGauge.IsAvailable = false;
                        }
                    }
                }
                catch { }
            });
        }

        private static void AnimateGauge(Controls.CircularGauge gauge, double targetValue)
        {
            var anim = new DoubleAnimation
            {
                From = gauge.Value,
                To = targetValue,
                Duration = TimeSpan.FromSeconds(0.6),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            gauge.BeginAnimation(Controls.CircularGauge.ValueProperty, anim);
        }

        private void UpdateLivePreview()
        {
            var s = ThemeSettings.Current;

            PreviewCard.Background = s.GetWindowBackground();
            PreviewCard.BorderBrush = s.GetWindowBorder();

            var textPrimary = s.GetTextPrimary();
            var textSecondary = s.GetTextSecondary();
            var trackBrush = s.GetTrackBrush();
            var progressBrush = s.GetProgressBrush();
            var budBrush = s.GetBudBrush();

            // Preview Text colors
            PreviewLeftPercent.Foreground = textPrimary;
            PreviewCasePercent.Foreground = textPrimary;
            PreviewRightPercent.Foreground = textPrimary;
            PreviewOverEarPercent.Foreground = textPrimary;

            PreviewLeftStatus.Foreground = textSecondary;
            PreviewCaseStatus.Foreground = textSecondary;
            PreviewRightStatus.Foreground = textSecondary;
            PreviewOverEarStatus.Foreground = textSecondary;

            // Preview Gauge colors
            PreviewLeftGauge.TrackBrush = trackBrush;
            PreviewLeftGauge.ProgressBrush = progressBrush;
            PreviewCaseGauge.TrackBrush = trackBrush;
            PreviewCaseGauge.ProgressBrush = progressBrush;
            PreviewRightGauge.TrackBrush = trackBrush;
            PreviewRightGauge.ProgressBrush = progressBrush;
            PreviewOverEarGauge.TrackBrush = trackBrush;
            PreviewOverEarGauge.ProgressBrush = progressBrush;

            // Preview Silhouettes
            PreviewLeftStem.Fill = budBrush;
            PreviewLeftHead.Fill = budBrush;
            PreviewLeftTip.Fill = budBrush;
            PreviewCaseBody.Fill = budBrush;
            PreviewRightStem.Fill = budBrush;
            PreviewRightHead.Fill = budBrush;
            PreviewRightTip.Fill = budBrush;

            PreviewOverEarArch.Stroke = budBrush;
            PreviewOverEarLeftCup.Fill = budBrush;
            PreviewOverEarRightCup.Fill = budBrush;

            // Overview Hero Card Silhouettes
            OverviewLeftStem.Fill = budBrush;
            OverviewLeftHead.Fill = budBrush;
            OverviewLeftTip.Fill = budBrush;
            OverviewCaseBody.Fill = budBrush;
            OverviewRightStem.Fill = budBrush;
            OverviewRightHead.Fill = budBrush;
            OverviewRightTip.Fill = budBrush;

            OverviewOverEarArch.Stroke = budBrush;
            OverviewOverEarLeftCup.Fill = budBrush;
            OverviewOverEarRightCup.Fill = budBrush;

            OverviewLeftGauge.ProgressBrush = progressBrush;
            OverviewCaseGauge.ProgressBrush = progressBrush;
            OverviewRightGauge.ProgressBrush = progressBrush;
            OverviewOverEarGauge.ProgressBrush = progressBrush;

            // Layout visibility based on auto-detected model type
            if (s.ModelType == HeadphoneModelType.OverEar)
            {
                OverviewTwsLayout.Visibility = Visibility.Collapsed;
                OverviewOverEarLayout.Visibility = Visibility.Visible;
                PreviewTwsLayout.Visibility = Visibility.Collapsed;
                PreviewOverEarLayout.Visibility = Visibility.Visible;
            }
            else
            {
                OverviewTwsLayout.Visibility = Visibility.Visible;
                OverviewOverEarLayout.Visibility = Visibility.Collapsed;
                PreviewTwsLayout.Visibility = Visibility.Visible;
                PreviewOverEarLayout.Visibility = Visibility.Collapsed;
            }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            ThemeSettings.Current.Theme = ThemeLightRadio.IsChecked == true ? AppTheme.Light : AppTheme.Dark;
            ThemeSettings.Current.Save();
            UpdateLivePreview();
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag && Enum.TryParse<BudColor>(tag, true, out var color))
            {
                ThemeSettings.Current.ColorPreset = color;
                ThemeSettings.Current.Save();
                UpdateLivePreview();
            }
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            ThemeSettings.Current.ShowBatteryInTray = ShowInTrayCheck.IsChecked == true;
            ThemeSettings.Current.Save();

            if (Application.Current is App app)
            {
                app.RefreshTrayIcon();
            }
        }

        private void Autorun_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            StartupManager.SetAutoStart(AutorunCheck.IsChecked == true);
        }

        private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;

            DurationLabel.Text = $"{e.NewValue:F1} с";
            ThemeSettings.Current.DisplayDurationSeconds = e.NewValue;
            ThemeSettings.Current.Save();
        }

        private void TopYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;

            TopYLabel.Text = $"{e.NewValue:F0} px";
            ThemeSettings.Current.TargetTopY = e.NewValue;
            ThemeSettings.Current.Save();
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (FullscreenBtnText != null)
            {
                FullscreenBtnText.Text = WindowState == WindowState.Maximized ? "Оконный режим" : "Во весь экран";
            }
        }

        private void TestDesktopButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                _ = app.TriggerDesktopPopupAsync();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Do not terminate application when settings window is closed; minimize to tray
            e.Cancel = true;
            Hide();
        }
    }
}