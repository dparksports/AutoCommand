using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoCommand.Services
{
    /// <summary>
    /// Dark / Light theme switching. Replaces application-level brush resources
    /// at runtime — every theme-brush reference in XAML is a DynamicResource, so
    /// the swap applies live without a restart. Choice persists in HKCU.
    ///
    /// Dark = deep-space gradient identity (violet→magenta→blue, from the icon).
    /// Light = Fluent adaptation of the reference UI: airy lilac-tinted canvas,
    /// pure-white floating cards, hairline borders, brand-violet accent —
    /// the reference's shape language is in App.xaml (pills, 16px cards,
    /// soft shadows) and is shared by both themes.
    /// </summary>
    public static class ThemeService
    {
        private const string RegKey = @"Software\AutoCommand";
        private const string RegName = "Theme";

        // (resource key, dark value, light value)
        private static readonly (string Key, string Dark, string Light)[] BrushMap =
        {
            ("BackgroundBrush",    "#140F2D", "#F3F2F8"),
            ("SurfaceBrush",       "#1C1640", "#FFFFFF"),
            ("SurfaceAltBrush",    "#271E52", "#F7F5FB"),
            ("BorderBrush",        "#3A2C6E", "#E3DEF0"),
            ("TextPrimaryBrush",   "#FFFFFF", "#1C1633"),
            ("TextSecondaryBrush", "#C3B8EE", "#6B6390"),
            ("AccentBlueBrush",    "#7C5CFF", "#6D28D9"),
            ("AccentGreenBrush",   "#34D399", "#059669"),
            ("AccentAmberBrush",   "#FBBF24", "#B45309"),
            ("AccentRedBrush",     "#F43F5E", "#DC2626"),
            ("AccentVioletBrush",  "#C084FC", "#7C3AED"),
            ("AccentPinkBrush",    "#EC4899", "#DB2777"),
            ("AccentCyanBrush",    "#22D3EE", "#0E7490"),
            ("NavSelectedBrush",   "#14FFFFFF", "#EBE6F8"),
            ("NavHoverBrush",      "#0BFFFFFF", "#F1EEFA"),
            ("RowAltBrush",        "#08FFFFFF", "#F8F6FC"),
            ("RowHoverBrush",      "#0CFFFFFF", "#EFEBF8"),
            ("RowSelectedBrush",   "#20FFFFFF", "#E3DCF6"),
            ("GridLineBrush",      "#18FFFFFF", "#EBE7F3"),
        };

        public static string Saved =>
            Registry.GetValue($@"HKEY_CURRENT_USER\{RegKey}", RegName, "Dark") as string ?? "Dark";

        /// <summary>Apply a theme now and remember it. theme: "Dark" | "Light".</summary>
        public static void ApplyTheme(string theme)
        {
            bool light = theme == "Light";
            var res = Application.Current.Resources;
            foreach (var (key, dark, lightVal) in BrushMap)
                res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? lightVal : dark));

            if (light)
            {
                // Fluent light: gentler gradients so white text keeps contrast
                res["PrimaryGradientBrush"] = MakeGradient("#7C3AED", "#9333EA", "#6D28D9");
                res["DangerGradientBrush"] = MakeGradient("#DC2626", "#DB2777");
                res["TabIndicatorGradientBrush"] = MakeGradient("#7C3AED", "#A855F7", "#DB2777");
            }
            else
            {
                res["PrimaryGradientBrush"] = MakeGradient("#8B5CF6", "#D946EF", "#4F8DFD");
                res["DangerGradientBrush"] = MakeGradient("#F43F5E", "#EC4899");
                res["TabIndicatorGradientBrush"] = MakeGradient("#22D3EE", "#C084FC", "#EC4899");
            }

            using var k = Registry.CurrentUser.CreateSubKey(RegKey);
            k.SetValue(RegName, light ? "Light" : "Dark");
        }

        private static LinearGradientBrush MakeGradient(params string[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            double step = stops.Length > 1 ? 1.0 / (stops.Length - 1) : 0;
            for (int i = 0; i < stops.Length; i++)
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i * step));
            return brush;
        }
    }
}
