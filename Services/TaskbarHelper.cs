using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiUsageMonitor.Services;

public static class TaskbarHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    public static void ApplyToolWindowStyle(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private static IntPtr _lastForeground;

    public static bool HasForegroundChanged()
    {
        var fg = GetForegroundWindow();
        if (fg == _lastForeground) return false;
        _lastForeground = fg;
        return true;
    }

    public static bool IsFullscreenForegroundApp()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        if (!GetWindowRect(fg, out var rect))
            return false;

        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return false;

        var bounds = screen.Bounds;
        if (rect.Left > bounds.Left ||
            rect.Top > bounds.Top ||
            rect.Right < bounds.Right ||
            rect.Bottom < bounds.Bottom)
            return false;

        var sb = new System.Text.StringBuilder(64);
        _ = GetClassName(fg, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls is "Shell_TrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow" or "TopLevelWindowForOverflowXamlIsland")
            return false;

        try
        {
            _ = GetWindowThreadProcessId(fg, out var pid);
            if (pid != 0)
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                if (proc.ProcessName is "explorer" or "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost" or "ShellHost")
                    return false;
            }
        }
        catch { }

        return true;
    }

    public static void PositionOnTaskbarLeft(Window window)
    {
        var (tbLeftPx, tbTopPx, tbHeightPx) = GetPrimaryTaskbarRectPx();

        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
            return;

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(tbLeftPx, tbTopPx));
        var bottomRight = transform.Transform(new System.Windows.Point(tbLeftPx, tbTopPx + tbHeightPx));

        var tbHeight = bottomRight.Y - topLeft.Y;
        window.Left = topLeft.X + 8;
        window.Top = topLeft.Y + Math.Max(0, (tbHeight - window.ActualHeight) / 2.0);

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    public static void EnsureTopMost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    public static (double Left, double Top) GetTaskbarTopLeftDip(Window referenceWindow)
    {
        var (tbLeftPx, tbTopPx, _) = GetPrimaryTaskbarRectPx();

        var source = PresentationSource.FromVisual(referenceWindow);
        if (source?.CompositionTarget is null)
            return (8, SystemParameters.WorkArea.Bottom);

        var dip = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(tbLeftPx, tbTopPx));
        return (dip.X + 8, dip.Y);
    }

    private static (int Left, int Top, int Height) GetPrimaryTaskbarRectPx()
    {
        var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd != IntPtr.Zero && GetWindowRect(taskbarHwnd, out var tbRect))
        {
            var h = tbRect.Bottom - tbRect.Top;
            if (h > 10 && tbRect.Right > tbRect.Left)
                return (tbRect.Left, tbRect.Top, h);
        }

        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is not null)
        {
            var tbHeight = screen.Bounds.Bottom - screen.WorkingArea.Bottom;
            if (tbHeight > 10)
                return (screen.Bounds.Left, screen.WorkingArea.Bottom, tbHeight);
        }

        return (0, 1392, 48);
    }

    public static string FormatShortCountdown(DateTimeOffset? resetUtc)
    {
        if (resetUtc is null) return "--";
        var diff = resetUtc.Value - DateTimeOffset.UtcNow;
        if (diff <= TimeSpan.Zero) return "0m";
        if (diff.TotalDays >= 1)
            return $"{(int)Math.Ceiling(diff.TotalDays)}d";
        if (diff.TotalHours >= 1)
            return $"{(int)diff.TotalHours}h{diff.Minutes:D2}";
        return $"{Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }

    public static string FormatLongCountdown(DateTimeOffset? resetUtc)
    {
        if (resetUtc is null) return "neznámý reset";
        var diff = resetUtc.Value - DateTimeOffset.UtcNow;
        if (diff <= TimeSpan.Zero) return "reset právě teď";
        if (diff.TotalDays >= 1)
            return $"za {(int)diff.TotalDays}d {diff.Hours}h";
        if (diff.TotalHours >= 1)
            return $"za {(int)diff.TotalHours}h {diff.Minutes:D2}m";
        return $"za {Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }
}
