using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AiUsageMonitor.Models;
using AiUsageMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace AiUsageMonitor;

public partial class MainWindow : Window
{
    private static readonly Brush PillNormalBg = new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
    private static readonly Brush PillHoverBg = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    private static readonly Brush WarnAccent = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush DangerAccent = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush NormalText = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SecondaryText = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TimerText = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    private static readonly Brush MutedText = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));

    private readonly AntigravityService _agService = new();
    private readonly ClaudeService _clService = new();
    private readonly FlyoutWindow _flyout = new();
    private readonly ContextMenuWindow _contextMenu = new();

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _fetchTimer;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _zOrderTimer;

    private AccountUsage _lastAg = AccountUsage.Unavailable("Antigravity", "Načítám...");
    private AccountUsage _lastCl = AccountUsage.Unavailable("Claude Code", "Načítám...");
    private bool _isFetching;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();

        _contextMenu.RefreshRequested += RefreshAllAsync;
        _contextMenu.DetailRequested += () => _flyout.ShowAnchored(this);
        _contextMenu.SettingsChanged += ApplySettings;
        _contextMenu.ExitRequested += () => System.Windows.Application.Current.Shutdown();

        _fetchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds) };
        _fetchTimer.Tick += async (_, _) => await RefreshAllAsync();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _uiTimer.Tick += (_, _) => OnUiTick();

        _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _zOrderTimer.Tick += (_, _) =>
        {
            if (IsVisible && TaskbarHelper.HasForegroundChanged())
                TaskbarHelper.EnsureTopMost(this);
        };

        ApplySettings();
    }

    private void ApplySettings()
    {
        AgBlock.Visibility = _settings.ShowAntigravity ? Visibility.Visible : Visibility.Collapsed;
        ClBlock.Visibility = _settings.ShowClaude ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = _settings.ShowAntigravity && _settings.ShowClaude
            ? Visibility.Visible
            : Visibility.Collapsed;

        _fetchTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        TaskbarHelper.ApplyToolWindowStyle(this);
        TaskbarHelper.PositionOnTaskbarLeft(this);

        _fetchTimer.Start();
        _uiTimer.Start();
        _zOrderTimer.Start();

        await RefreshAllAsync();
    }

    private void OnUiTick()
    {
        if (TaskbarHelper.IsFullscreenForegroundApp())
        {
            if (IsVisible) Hide();
            return;
        }

        if (!IsVisible) Show();
        TaskbarHelper.PositionOnTaskbarLeft(this);
        RenderCountdowns();
    }

    public async Task RefreshAllAsync()
    {
        if (_isFetching) return;
        _isFetching = true;
        try
        {
            var agTask = _settings.ShowAntigravity
                ? _agService.FetchAsync()
                : Task.FromResult(AccountUsage.Unavailable("Antigravity", "Skryto v nastavení"));
            var clTask = _settings.ShowClaude
                ? _clService.FetchAsync()
                : Task.FromResult(AccountUsage.Unavailable("Claude Code", "Skryto v nastavení"));

            await Task.WhenAll(agTask, clTask);

            _lastAg = agTask.Result;
            _lastCl = clTask.Result;

            RenderAll();
            _flyout.UpdateData(_lastAg, _lastCl);
        }
        finally
        {
            _isFetching = false;
        }
    }

    private void RenderAll()
    {
        RenderAccount(_lastAg, Ag5hPct, Ag5hReset, AgWPct, AgWReset);
        RenderAccount(_lastCl, Cl5hPct, Cl5hReset, ClWPct, ClWReset);
    }

    private void RenderCountdowns()
    {
        Ag5hReset.Text = _lastAg.IsAvailable ? TaskbarHelper.FormatShortCountdown(_lastAg.FiveHourResetUtc) : "--";
        AgWReset.Text = _lastAg.IsAvailable ? TaskbarHelper.FormatShortCountdown(_lastAg.WeeklyResetUtc) : "--";
        Cl5hReset.Text = _lastCl.IsAvailable ? TaskbarHelper.FormatShortCountdown(_lastCl.FiveHourResetUtc) : "--";
        ClWReset.Text = _lastCl.IsAvailable ? TaskbarHelper.FormatShortCountdown(_lastCl.WeeklyResetUtc) : "--";
    }

    private static void RenderAccount(
        AccountUsage usage,
        TextBlock fivePctText, TextBlock fiveResetText,
        TextBlock weekPctText, TextBlock weekResetText)
    {
        if (!usage.IsAvailable)
        {
            fivePctText.Text = "--%";
            fivePctText.Foreground = MutedText;
            fiveResetText.Text = "--";
            fiveResetText.Foreground = MutedText;

            weekPctText.Text = "--%";
            weekPctText.Foreground = MutedText;
            weekResetText.Text = "--";
            return;
        }

        var fivePct = usage.FiveHourUsedPercent ?? 0;
        var fiveBrush = fivePct >= 95 ? DangerAccent : fivePct >= 80 ? WarnAccent : NormalText;
        fivePctText.Text = $"{Math.Round(fivePct):0}%";
        fivePctText.Foreground = fiveBrush;
        fiveResetText.Text = TaskbarHelper.FormatShortCountdown(usage.FiveHourResetUtc);
        fiveResetText.Foreground = fivePct >= 80 ? fiveBrush : TimerText;

        var weekPct = usage.WeeklyUsedPercent ?? 0;
        var weekBrush = weekPct >= 95 ? DangerAccent : weekPct >= 80 ? WarnAccent : SecondaryText;
        weekPctText.Text = $"{Math.Round(weekPct):0}%";
        weekPctText.Foreground = weekBrush;
        weekResetText.Text = TaskbarHelper.FormatShortCountdown(usage.WeeklyResetUtc);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_contextMenu.IsVisible) _contextMenu.Hide();

        if (_flyout.IsVisible)
            _flyout.Hide();
        else
            _flyout.ShowAnchored(this);
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_flyout.IsVisible) _flyout.Hide();
        _contextMenu.ShowAtCursor(this, _settings);
    }

    private void RootPill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        RootPill.Background = PillHoverBg;

    private void RootPill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        RootPill.Background = PillNormalBg;
}
