using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Glasshouse
{
    public static class Colors
    {
        public static string Normalise(string hex)
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2);   // AARRGGBB -> RRGGBB
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, null, out _)) throw new FormatException("not a colour: " + hex);
            return "#" + hex.ToUpperInvariant();
        }

        public static (int r, int g, int b) Rgb(string hex)
        {
            hex = Normalise(hex).Substring(1);
            return (Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        public static string Hex(int r, int g, int b) => $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}";
        static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

        // Windows stores colours as 0xAABBGGRR
        public static uint Abgr(string hex) { var (r, g, b) = Rgb(hex); return 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | (uint)r; }
        public static string FromAbgr(uint v) => Hex((int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF));

        public static (double h, double s, double l) Hsl(string hex)
        {
            var (r0, g0, b0) = Rgb(hex);
            double r = r0 / 255.0, g = g0 / 255.0, b = b0 / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0); else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
                h /= 6;
            }
            return (h, s, l);
        }

        public static string FromHsl(double h, double s, double l)
        {
            l = Math.Max(0, Math.Min(1, l)); s = Math.Max(0, Math.Min(1, s));
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double f(double t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1.0 / 6) return p + (q - p) * 6 * t; if (t < 0.5) return q; if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6; return p; }
            return s == 0 ? Hex((int)Math.Round(l * 255), (int)Math.Round(l * 255), (int)Math.Round(l * 255))
                          : Hex((int)Math.Round(f(h + 1.0 / 3) * 255), (int)Math.Round(f(h) * 255), (int)Math.Round(f(h - 1.0 / 3) * 255));
        }

        public static string Shade(string hex, double dl, double sMul = 1)
        {
            var (h, s, l) = Hsl(hex);
            return FromHsl(h, s * sMul, l + dl);
        }
    }

    // The Windows accent colour (switches, the faint bar on the top search result, highlights). Per machine:
    // (two of the colours offered in Glasshouse: Lava Orange #F05A22 and Blue #2283F0)
    public static class Accent
    {
        [StructLayout(LayoutKind.Sequential)] struct ImmersiveColorPreference { public uint Start; public uint Accent; }
        [DllImport("uxtheme.dll", EntryPoint = "GetUserColorPreference")] static extern int GetPref(out ImmersiveColorPreference p, bool forceReload);
        [DllImport("uxtheme.dll", EntryPoint = "SetUserColorPreference")] static extern int SetPref(ref ImmersiveColorPreference p, bool forceCommit);

        public static string Current()
        {
            try { if (GetPref(out var p, false) == 0) return Colors.FromAbgr(p.Accent); } catch { }
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                return k?.GetValue("AccentColor") is int v ? Colors.FromAbgr(unchecked((uint)v)) : null;
        }

        // Approximates Windows' own 7-step palette (lighter 3, the accent's working shade, darker 3). Checked against
        // the palettes Windows made for #F05A22 and #2283F0 on test machines; Windows replaces it with its own
        // when the colour is applied through its theme engine (Auto Dark Mode does that).
        public static byte[] Palette(string hex)
        {
            double[] steps = { 0.18, 0.10, -0.05, -0.10, -0.16, -0.25, -0.34 };
            var bytes = new byte[32];
            for (int i = 0; i < 7; i++)
            {
                var (r, g, b) = Colors.Rgb(Colors.Shade(hex, steps[i], 1.05));
                bytes[i * 4] = (byte)r; bytes[i * 4 + 1] = (byte)g; bytes[i * 4 + 2] = (byte)b; bytes[i * 4 + 3] = 0;
            }
            bytes[28] = 0x88; bytes[29] = 0x17; bytes[30] = 0x98; bytes[31] = 0x00;   // Windows' fixed 8th entry
            return bytes;
        }

        public static void Apply(string hex)
        {
            hex = Colors.Normalise(hex);
            string start = Colors.Shade(hex, -0.16, 1.05);   // Windows' darker working shade (palette step 5)
            var pal = Palette(hex);
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
            {
                k.SetValue("AccentPalette", pal, RegistryValueKind.Binary);
                k.SetValue("AccentColorMenu", unchecked((int)Colors.Abgr(hex)), RegistryValueKind.DWord);
                k.SetValue("StartColorMenu", unchecked((int)Colors.Abgr(start)), RegistryValueKind.DWord);
            }
            var (r, g, b) = Colors.Rgb(hex);
            int argb = unchecked((int)(0xC4000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                k.SetValue("AccentColor", unchecked((int)Colors.Abgr(hex)), RegistryValueKind.DWord);
                k.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
                k.SetValue("ColorizationAfterglow", argb, RegistryValueKind.DWord);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                k.SetValue("AutoColorization", 0, RegistryValueKind.DWord);   // don't pick the colour from the wallpaper
            // The same call Windows' Settings makes: it commits the colour and tells every app, no restart needed.
            try
            {
                var p = new ImmersiveColorPreference { Start = Colors.Abgr(start), Accent = Colors.Abgr(hex) };
                int hr = SetPref(ref p, true);
                if (hr != 0) Log.Warn($"SetUserColorPreference returned 0x{hr:X}");
            }
            catch (Exception e) { Log.Warn("SetUserColorPreference: " + e.Message); }
            Shell.Broadcast("ImmersiveColorSet");
            AutoDarkMode.SetAccent(hex);   // or Auto Dark Mode would put its own colour back at the next switch
            Log.Info("accent colour " + hex);
        }
    }
}
