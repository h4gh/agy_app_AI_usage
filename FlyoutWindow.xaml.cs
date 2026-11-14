using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiUsageMonitor.Models;
using AiUsageMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace AiUsageMonitor;

public partial class FlyoutWindow : Window
{
    private static readonly Brush AgBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
    private static readonly Brush ClBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    public FlyoutWindow()
    {
        InitializeComponent();
    }

    public void UpdateData(AccountUsage ag, AccountUsage cl)
    {
        UpdatedText.Text = $"Aktualizováno {DateTime.Now:HH:mm:ss}";
        PopulatePanel(AgItemsPanel, ag, AgBrush, isAntigravity: true);
        PopulatePanel(ClItemsPanel, cl, ClBrush, isAntigravity: false);
    }

    public void ShowAnchored(Window anchorWindow)
    {
        var (left, tbTop) = TaskbarHelper.GetTaskbarTopLeftDip(anchorWindow);
        Left = left;
        if (!IsVisible)
            Show();
        UpdateLayout();
        Top = Math.Max(8, tbTop - ActualHeight - 8);
        Activate();
    }

    private static void PopulatePanel(System.Windows.Controls.Panel panel, AccountUsage usage, Brush accent, bool isAntigravity)
    {
        panel.Children.Clear();

        if (!usage.IsAvailable)
        {
            panel.Children.Add(new TextBlock
            {
                Text = usage.StatusMessage ?? "Nedostupné",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var items = isAntigravity
            ? usage.Models
                .OrderByDescending(m => m.Name.Contains("Hydrogen", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(m => m.Name.Contains("Argon", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList()
            : usage.Models.ToList();

        for (var i = 0; i < items.Count; i++)
        {
            var m = items[i];
            var barColor = m.UsedPercent >= 95 ? DangerBrush : m.UsedPercent >= 80 ? WarnBrush : accent;

            var container = new StackPanel { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };

            var topGrid = new Grid();
            topGrid.Children.Add(new TextBlock
            {
                Text = ShortenModelName(m.Name),
                Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
                FontSize = 11,
                FontWeight = FontWeights.Medium
            });
            topGrid.Children.Add(new TextBlock
            {
                Text = $"{m.UsedPercent:0.#}%",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Foreground = barColor,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
            container.Children.Add(topGrid);

            var track = new Border
            {
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = TrackBrush,
                Margin = new Thickness(0, 3, 0, 2),
                ClipToBounds = true
            };
            track.Child = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = Math.Max(2, 165 * (Math.Clamp(m.UsedPercent, 0, 100) / 100.0)),
                Background = barColor,
                CornerRadius = new CornerRadius(2.5)
            };
            container.Children.Add(track);

            container.Children.Add(new TextBlock
            {
                Text = $"Reset {TaskbarHelper.FormatLongCountdown(m.ResetUtc)}",
                Foreground = new SolidColorBrush(Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF)),
                FontSize = 10
            });

            panel.Children.Add(container);
        }
    }

    private static string ShortenModelName(string name)
    {
        var shortName = name
            .Replace("Gemini 4 ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Gemini 3.8 ", "G3.8 ", StringComparison.OrdinalIgnoreCase);

        if (shortName.Equals("Hydrogen", StringComparison.OrdinalIgnoreCase))
            return "Hydrogen (5h)";
        if (shortName.Equals("Argon", StringComparison.OrdinalIgnoreCase))
            return "Argon (Weekly)";
        if (shortName.Equals("Orchestrator", StringComparison.OrdinalIgnoreCase))
            return "Orchestrator (Weekly)";
        if (shortName.Equals("G3.8 Flash", StringComparison.OrdinalIgnoreCase))
            return "G3.8 Flash (5h)";

        return shortName;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Hide();
    }
}
