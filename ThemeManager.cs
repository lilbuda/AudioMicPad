using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioMicPad;

public enum AppTheme
{
    System,
    Dark,
    Light
}

internal static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Apply(AppTheme theme, Window window)
    {
        var isDark = theme == AppTheme.Dark || theme == AppTheme.System && IsSystemDark();
        var colors = isDark ? DarkColors : LightColors;

        foreach (var (key, color) in colors)
            System.Windows.Application.Current.Resources[key] = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

        window.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["WindowBackgroundBrush"];
        window.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PrimaryTextBrush"];
        ApplyTitleBar(window, isDark);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyTitleBar(Window window, bool isDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var value = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
    }

    private static readonly IReadOnlyDictionary<string, string> LightColors = new Dictionary<string, string>
    {
        ["WindowBackgroundBrush"] = "#F4F7FB",
        ["SurfaceBrush"] = "#FFFFFF",
        ["ElevatedSurfaceBrush"] = "#EEF3FA",
        ["BorderBrush"] = "#CCD5E2",
        ["PrimaryTextBrush"] = "#172033",
        ["SecondaryTextBrush"] = "#5D6B82",
        ["AccentBrush"] = "#315EDE",
        ["AccentHoverBrush"] = "#244BB8",
        ["SelectionBrush"] = "#DDE7FF",
        ["DangerBrush"] = "#C9364F"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkColors = new Dictionary<string, string>
    {
        ["WindowBackgroundBrush"] = "#0B1020",
        ["SurfaceBrush"] = "#121A2B",
        ["ElevatedSurfaceBrush"] = "#182338",
        ["BorderBrush"] = "#2D3A52",
        ["PrimaryTextBrush"] = "#F3F6FC",
        ["SecondaryTextBrush"] = "#AAB6CB",
        ["AccentBrush"] = "#7C9CFF",
        ["AccentHoverBrush"] = "#94AEFF",
        ["SelectionBrush"] = "#294078",
        ["DangerBrush"] = "#F07178"
    };
}
