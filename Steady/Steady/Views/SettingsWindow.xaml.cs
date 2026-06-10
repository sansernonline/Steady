using System.Windows;
using System.Windows.Controls;
using Steady.Helpers;
using Steady.Models;
using Steady.Services;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Steady.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private bool _loading;

    public event EventHandler? SettingsChanged;

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _loading = true; // prevent event handlers firing on uninitialized controls during InitializeComponent
        InitializeComponent();
        VersionLabel.Text = $"v{AppInfo.Version}";
        LoadSettings();
    }

    // Make the OS title bar follow the Windows light/dark theme.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Win32Helper.ApplyTitleBarTheme(hwnd, Win32Helper.IsSystemDarkTheme());
    }

    private void LoadSettings()
    {
        _loading = true;
        var s = _settingsService.Current;

        DotCountSlider.Value = s.DotSpacing;
        DotSizeSlider.Value = s.DotSize;
        OpacitySlider.Value = s.DotOpacity;
        IntensitySlider.Value = s.IntensityMultiplier;
        CameraEnabledCheck.IsChecked = s.CameraEnabled;
        AdaptiveContrastCheck.IsChecked = s.AdaptiveContrast;
        StartupCheck.IsChecked = s.RunAtStartup;
        AutoActivationCheck.IsChecked = s.AutoActivation;
        AutoThresholdSlider.Value = s.AutoActivationThreshold;
        AutoActivationPanel.Visibility = s.AutoActivation ? Visibility.Visible : Visibility.Collapsed;
        BatterySaverCheck.IsChecked = s.BatterySaverEnabled;
        LowLightCheck.IsChecked = s.LowLightEnhancement;
        AdaptiveThresholdCheck.IsChecked = s.AdaptiveThreshold;

        SelectComboByTag(TierCombo, s.PreferredTier.ToString());
        SelectComboByTag(ColorCombo, s.DotColor);

        UpdateLabels();
        _loading = false;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    public void UpdateActiveTierLabel(ActiveSensorTier tier)
    {
        ActiveTierLabel.Text = tier switch
        {
            ActiveSensorTier.Gyro        => "Active sensor: Gyro / Accelerometer (Tier 1)",
            ActiveSensorTier.OpticalFlow => "Active sensor: Camera optical flow (Tier 2)",
            ActiveSensorTier.Camera      => "Active sensor: Camera head-tracking (Tier 2 alt)",
            ActiveSensorTier.Mic         => "Active sensor: Microphone noise level (Tier 3)",
            _                            => "Active sensor: None — no sensor available"
        };
    }

    private void UpdateLabels()
    {
        DotCountLabel.Text = $"{(int)DotCountSlider.Value}px";
        DotSizeLabel.Text = $"{DotSizeSlider.Value:F0}px";
        OpacityLabel.Text = $"{OpacitySlider.Value * 100:F0}%";
        IntensityLabel.Text = $"{IntensitySlider.Value:F1}x";
        AutoThresholdLabel.Text = $"{AutoThresholdSlider.Value:F2}g";
    }

    private void ApplyToSettings()
    {
        if (_loading) return;
        var s = _settingsService.Current;
        s.DotSpacing = DotCountSlider.Value;
        s.DotSize = DotSizeSlider.Value;
        s.DotOpacity = OpacitySlider.Value;
        s.IntensityMultiplier = IntensitySlider.Value;
        s.CameraEnabled = CameraEnabledCheck.IsChecked == true;
        s.AdaptiveContrast = AdaptiveContrastCheck.IsChecked == true;
        s.AutoActivation = AutoActivationCheck.IsChecked == true;
        s.AutoActivationThreshold = AutoThresholdSlider.Value;
        s.BatterySaverEnabled = BatterySaverCheck.IsChecked == true;
        s.LowLightEnhancement = LowLightCheck.IsChecked == true;
        s.AdaptiveThreshold = AdaptiveThresholdCheck.IsChecked == true;
        s.DotColor = (ColorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#FFFFFF";
        s.PreferredTier = Enum.TryParse<SensorTierPreference>(
            (TierCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var t) ? t : SensorTierPreference.Auto;
    }

    private void DotCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_loading) return; UpdateLabels(); ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); }

    private void DotSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_loading) return; UpdateLabels(); ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_loading) return; UpdateLabels(); ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); }

    private void IntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_loading) return; UpdateLabels(); ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); }

    private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (_loading) return; ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); }

    private void TierCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading) ApplyToSettings(); }

    private void CameraEnabledCheck_Changed(object sender, RoutedEventArgs e)
    { ApplyToSettings(); }

    private void AdaptiveContrastCheck_Changed(object sender, RoutedEventArgs e)
    { if (!_loading) { ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); } }

    private void StartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settingsService.SetStartup(StartupCheck.IsChecked == true);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
        _settingsService.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void BatterySaverCheck_Changed(object sender, RoutedEventArgs e)
    { if (!_loading) { ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); } }

    private void LowLightCheck_Changed(object sender, RoutedEventArgs e)
    { if (!_loading) { ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); } }

    private void AdaptiveThresholdCheck_Changed(object sender, RoutedEventArgs e)
    { if (!_loading) { ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); } }

    private void AutoActivationCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AutoActivationPanel.Visibility = AutoActivationCheck.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateLabels();
        ApplyToSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AutoThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_loading) return; UpdateLabels(); ApplyToSettings(); SettingsChanged?.Invoke(this, EventArgs.Empty); }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
