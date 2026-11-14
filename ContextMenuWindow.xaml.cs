using System.Windows;
using System.Windows.Media;
using AiUsageMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace AiUsageMonitor;

public partial class ContextMenuWindow : Window
{
    private static readonly Brush ActivePillBg = new SolidColorBrush(Color.FromArgb(0x30, 0x34, 0xD3, 0x99));
    private static readonly Brush ActivePillFg = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
    private static readonly Brush InactivePillBg = new SolidColorBrush(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush InactivePillFg = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

    private AppSettings _settings = new();

    public event Func<Task>? RefreshRequested;
    public event Action? DetailRequested;
    public event Action? SettingsChanged;
    public event Action? ExitRequested;

    public ContextMenuWindow()
    {
        InitializeComponent();
    }

    public void ShowAtCursor(Window anchorWindow, AppSettings settings)
    {
        _settings = settings;
        SyncUiState();

        var (left, tbTop) = TaskbarHelper.GetTaskbarTopLeftDip(anchorWindow);
        Left = left;
        if (!IsVisible)
            Show();
        UpdateLayout();
        Top = Math.Max(8, tbTop - ActualHeight - 6);
        Activate();
    }

    private void SyncUiState()
    {
        StartupCheck.Visibility = StartupService.IsEnabled() ? Visibility.Visible : Visibility.Hidden;
        AgCheck.Visibility = _settings.ShowAntigravity ? Visibility.Visible : Visibility.Hidden;
        ClCheck.Visibility = _settings.ShowClaude ? Visibility.Visible : Visibility.Hidden;

        SetPillState(Btn30s, _settings.RefreshSeconds == 30);
        SetPillState(Btn1m, _settings.RefreshSeconds == 60);
        SetPillState(Btn2m, _settings.RefreshSeconds == 120);
        SetPillState(Btn5m, _settings.RefreshSeconds == 300);
    }

    private static void SetPillState(Button btn, bool active)
    {
        btn.Background = active ? ActivePillBg : InactivePillBg;
        btn.Foreground = active ? ActivePillFg : InactivePillFg;
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Hide();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        if (RefreshRequested is not null)
            await RefreshRequested.Invoke();
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        DetailRequested?.Invoke();
    }

    private void ToggleAg_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ShowAntigravity && !_settings.ShowClaude)
            return;

        _settings.ShowAntigravity = !_settings.ShowAntigravity;
        SettingsService.Save(_settings);
        SyncUiState();
        SettingsChanged?.Invoke();
    }

    private void ToggleCl_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ShowClaude && !_settings.ShowAntigravity)
            return;

        _settings.ShowClaude = !_settings.ShowClaude;
        SettingsService.Save(_settings);
        SyncUiState();
        SettingsChanged?.Invoke();
    }

    private void Interval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var seconds))
        {
            _settings.RefreshSeconds = seconds;
            SettingsService.Save(_settings);
            SyncUiState();
            SettingsChanged?.Invoke();
        }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        var next = !StartupService.IsEnabled();
        StartupService.SetEnabled(next);
        Hide();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ExitRequested?.Invoke();
    }
}
