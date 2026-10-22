using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Glasshouse.Ui
{
    // Light or dark like the rest of the apps (they can switch at sunset), with this machine's accent colour.
    public static class Theme
    {
        public static bool Dark { get; private set; }
        public static event Action Changed;

        static readonly (string key, string dark, string light)[] Palette =
        {
            ("Win", "#202022", "#F6F6F7"), ("Side", "#28282B", "#EBEBED"), ("Bg", "#1E1E20", "#F3F3F3"), ("Group", "#2C2C2F", "#FFFFFF"),
            ("Line", "#3A3A3D", "#E3E3E6"), ("Text", "#F2F2F2", "#1D1D1F"), ("Sub", "#A1A1A6", "#6E6E73"), ("Sel", "#3A3A3E", "#DCDCE0"),
            ("Hover", "#323235", "#E3E3E6"), ("Ctl", "#3F3F43", "#E9E9EC"), ("SegOn", "#5A5A5F", "#FFFFFF"), ("ToggleOff", "#58585C", "#D0D0D4"),
            ("TrackOff", "#58585C", "#D5D5D9"), ("CheckLine", "#77777C", "#A9A9AE"), ("Scroll", "#55FFFFFF", "#44000000"),
            ("Shade", "#59000000", "#40000000"), ("Warn", "#26FF9F0A", "#1FFF9F0A"), ("WarnLine", "#59FF9F0A", "#66FF9F0A"),
            ("Good", "#34C759", "#28A745"), ("Bad", "#FF453A", "#E5372C"),
        };

        public static void Init(Application app)
        {
            app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/Glasshouse;component/src/ui/styles.xaml", UriKind.Relative)));
            Refresh(app);
            SystemEvents.UserPreferenceChanged += (s, e) => app.Dispatcher.BeginInvoke(new Action(() => Refresh(app)));
        }

        public static void Refresh(Application app)
        {
            bool dark;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                dark = (k?.GetValue("AppsUseLightTheme") as int? ?? 1) == 0;
            Dark = dark;
            foreach (var (key, d, l) in Palette) app.Resources[key] = Brush(dark ? d : l);
            SetAccent(app, Glasshouse.Accent.Current() ?? "#F05A22");
            Changed?.Invoke();
        }

        public static void Force(Application app, bool dark)
        {
            Dark = dark;
            foreach (var (key, d, l) in Palette) app.Resources[key] = Brush(dark ? d : l);
        }

        public static void SetAccent(Application app, string hex) => app.Resources["Accent"] = Brush(hex);

        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ---------- the window's own title bar in the same colours (Windows 11) ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        public static void StyleTitleBar(Window w, bool? dark = null)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            bool isDark = dark ?? Dark;
            StyleTitleBar(h, isDark);
        }

        static void StyleTitleBar(IntPtr h, bool isDark)
        {
            int dark = isDark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref dark, 4);                       // dark title bar
            int caption = ColorRef(isDark ? "#202022" : "#F2F2F5");
            DwmSetWindowAttribute(h, 35, ref caption, 4);                    // caption colour = the window
            int text = ColorRef(isDark ? "#A1A1A6" : "#5A5A60");
            DwmSetWindowAttribute(h, 36, ref text, 4);                       // caption text
            int border = ColorRef(isDark ? "#3A3A3D" : "#C7C7CC");
            DwmSetWindowAttribute(h, 34, ref border, 4);
        }

        static int ColorRef(string hex) { var (r, g, b) = Glasshouse.Colors.Rgb(hex); return r | (g << 8) | (b << 16); }
    }
}
