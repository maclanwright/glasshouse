using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Glasshouse
{
    // The ready-made apps the look is built from. Each one installs from its own official source at setup time;
    // Glasshouse never carries anyone else's programs or Apple's pictures inside it.
    public static class Winget
    {
        public static string Exe
        {
            get
            {
                var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
                return File.Exists(p) ? p : "winget.exe";
            }
        }

        public static bool Available { get { try { return Shell.Run(Exe, "--version", 30).code == 0; } catch { return false; } } }

        public static bool Install(string id, string source = "winget", string scope = null)
        {
            string args = $"install --id {id} --exact --source {source} --silent --accept-package-agreements --accept-source-agreements --disable-interactivity"
                        + (scope != null ? " --scope " + scope : "");
            Log.Info($"installing {id} from {(source == "msstore" ? "the Microsoft Store" : "winget")}...");
            var (code, output) = Shell.Run(Exe, args, 1200);
            var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
            // -1978335189 = already installed, nothing newer
            bool ok = code == 0 || code == -1978335189 || last.IndexOf("already installed", StringComparison.OrdinalIgnoreCase) >= 0;
            if (ok) Log.Info($"  {id}: {last}"); else Log.Error($"  {id} failed ({code}): {last}");
            return ok;
        }
    }

    public static class AppPaths
    {
        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static string AdmDir => Path.Combine(Local, @"Programs\AutoDarkMode");
        public static string AdmConfig => Path.Combine(Roaming, @"AutoDarkMode\config.yaml");
        public static string WddDir => Path.Combine(Local, @"Programs\WinDynamicDesktop");
        public static string PowerToysExe => new[] { Path.Combine(Local, @"PowerToys\PowerToys.exe"), @"C:\Program Files\PowerToys\PowerToys.exe" }.FirstOrDefault(File.Exists);
        public static string PowerToysSettings => Path.Combine(Local, @"Microsoft\PowerToys\settings.json");
        public static bool RaycastInstalled => Directory.Exists(Path.Combine(Local, @"Packages\Raycast.Raycast_qypenmj9wpt2a"));
        public static bool FileConverterInstalled => Uninstall.Find("File Converter") != null;
    }

    public static class Uninstall
    {
        public static string Find(string displayNamePrefix)
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    using (var k = hive.OpenSubKey(path))
                        if (k != null)
                            foreach (var sub in k.GetSubKeyNames())
                                using (var s = k.OpenSubKey(sub))
                                    if (s?.GetValue("DisplayName") is string n && n.StartsWith(displayNamePrefix, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }
    }

    // ---------- Auto Dark Mode: apps switch light/dark with the sun, Windows' own parts stay dark ----------
    public static class AutoDarkMode
    {
        public static bool Installed => File.Exists(Path.Combine(AppPaths.AdmDir, @"adm-app\core\AutoDarkModeSvc.exe"));
        public static bool Running => Shell.Running("AutoDarkModeSvc");

        static readonly Dictionary<string, string> ModeIn = new Dictionary<string, string> { ["Switch"] = "switch", ["LightOnly"] = "light", ["DarkOnly"] = "dark" };
        static string ModeOut(string m) => m == "light" ? "LightOnly" : m == "dark" ? "DarkOnly" : "Switch";

        public static LightDark Read()
        {
            if (!File.Exists(AppPaths.AdmConfig)) return null;
            var y = new Yaml(File.ReadAllText(AppPaths.AdmConfig));
            var ld = new LightDark
            {
                Enabled = y.Get("AutoThemeSwitchingEnabled") == "true",
                Apps = ModeIn.TryGetValue(y.Get("AppsSwitch.Component.Mode") ?? "", out var a) ? a : "switch",
                Windows = ModeIn.TryGetValue(y.Get("SystemSwitch.Component.Mode") ?? "", out var s) ? s : "switch",
                CustomTimes = y.Get("Location.Enabled") != "true",
                UseWindowsLocation = y.Get("Location.Enabled") == "true" && y.Get("Location.UseGeolocatorService") == "true",
            };
            double.TryParse(y.Get("Location.CustomLat"), NumberStyles.Float, CultureInfo.InvariantCulture, out ld.Lat);
            double.TryParse(y.Get("Location.CustomLon"), NumberStyles.Float, CultureInfo.InvariantCulture, out ld.Lon);
            if (DateTime.TryParse(y.Get("Sunrise"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var sr)) ld.LightAt = sr.ToString("HH:mm");
            if (DateTime.TryParse(y.Get("Sunset"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ss)) ld.DarkAt = ss.ToString("HH:mm");
            return ld;
        }

        public static void Apply(LightDark ld)
        {
            if (!Installed) { Log.Warn("Auto Dark Mode isn't installed"); return; }
            EnsureConfig();
            var text = File.ReadAllText(AppPaths.AdmConfig);
            var y = new Yaml(text);
            y.Set("AutoThemeSwitchingEnabled", ld.Enabled ? "true" : "false");
            y.Set("AppsSwitch.Enabled", "true");
            y.Set("AppsSwitch.Component.Mode", ModeOut(ld.Apps));
            y.Set("SystemSwitch.Enabled", "true");
            y.Set("SystemSwitch.Component.Mode", ModeOut(ld.Windows));
            y.Set("WallpaperSwitch.Enabled", "false");   // the wallpaper app owns the wallpaper; two owners fight
            if (ld.CustomTimes)
            {
                y.Set("Location.Enabled", "false");
                y.Set("Sunrise", "2026-01-01T" + ld.LightAt + ":00.0000000");
                y.Set("Sunset", "2026-01-01T" + ld.DarkAt + ":00.0000000");
            }
            else if (ld.UseWindowsLocation)
            {
                y.Set("Location.Enabled", "true");
                y.Set("Location.UseGeolocatorService", "true");
            }
            else
            {
                y.Set("Location.Enabled", "true");
                y.Set("Location.UseGeolocatorService", "false");
                y.Set("Location.CustomLat", ld.Lat.ToString(CultureInfo.InvariantCulture));
                y.Set("Location.CustomLon", ld.Lon.ToString(CultureInfo.InvariantCulture));
            }
            Save(y, text);
        }

        public static void SetAccent(string hex)
        {
            if (!File.Exists(AppPaths.AdmConfig)) return;
            var text = File.ReadAllText(AppPaths.AdmConfig);
            var y = new Yaml(text);
            if (y.Get("ColorizationSwitch.Enabled") != "true") return;
            var v = "'#FF" + Colors.Normalise(hex).Substring(1) + "'";
            y.Set("ColorizationSwitch.Component.LightHex", v);
            y.Set("ColorizationSwitch.Component.DarkHex", v);
            Save(y, text);
        }

        static void Save(Yaml y, string before)
        {
            var after = y.ToString();
            if (after == before) return;
            Backup.SaveFile(AppPaths.AdmConfig);
            File.WriteAllText(AppPaths.AdmConfig, after, new UTF8Encoding(false));   // Auto Dark Mode reloads it by itself
            Log.Info("Auto Dark Mode settings updated");
        }

        // A fresh install writes its config the first time it runs.
        static void EnsureConfig()
        {
            if (File.Exists(AppPaths.AdmConfig)) return;
            var exe = Path.Combine(AppPaths.AdmDir, @"adm-app\core\AutoDarkModeSvc.exe");
            if (!Running) Shell.AsUser(exe, "", Path.GetDirectoryName(exe));
            for (int i = 0; i < 60 && !File.Exists(AppPaths.AdmConfig); i++) System.Threading.Thread.Sleep(500);
            System.Threading.Thread.Sleep(1500);
        }
    }

    // A tiny editor for Auto Dark Mode's config: changes "Section.Sub.Key: value" lines in place and leaves every
    // other line (and comment) exactly as it was. Only handles the plain nested maps that file uses.
    public class Yaml
    {
        readonly List<string> lines;
        public Yaml(string text) { lines = text.Replace("\r\n", "\n").Split('\n').ToList(); }
        public override string ToString() => string.Join(Environment.NewLine, lines);

        static int Indent(string l) => l.Length - l.TrimStart(' ').Length;

        int Find(string path, out int indent)
        {
            var parts = path.Split('.');
            int start = 0, end = lines.Count, ind = 0, at = -1;
            foreach (var p in parts)
            {
                at = -1;
                for (int i = start; i < end; i++)
                {
                    var l = lines[i];
                    if (l.Trim().Length == 0 || l.TrimStart().StartsWith("#")) continue;
                    if (Indent(l) < ind) break;
                    if (Indent(l) == ind && (l.Substring(ind).StartsWith(p + ":"))) { at = i; break; }
                }
                if (at < 0) { indent = ind; return -1; }
                start = at + 1;
                end = start;
                while (end < lines.Count && (lines[end].Trim().Length == 0 || Indent(lines[end]) > ind)) end++;
                ind += 2;
            }
            indent = ind - 2;
            return at;
        }

        public string Get(string path)
        {
            int i = Find(path, out int ind);
            if (i < 0) return null;
            var v = lines[i].Substring(lines[i].IndexOf(':') + 1).Trim();
            return v.Length >= 2 && (v[0] == '\'' || v[0] == '"') ? v.Substring(1, v.Length - 2) : v;
        }

        public void Set(string path, string value)
        {
            int i = Find(path, out int ind);
            if (i >= 0) { lines[i] = lines[i].Substring(0, lines[i].IndexOf(':') + 1) + " " + value; return; }
            // missing: add it at the end of its parent section (or the file)
            var parts = path.Split('.');
            string parent = string.Join(".", parts.Take(parts.Length - 1));
            if (parent.Length == 0) { lines.Insert(lines.Count > 0 && lines[lines.Count - 1] == "" ? lines.Count - 1 : lines.Count, $"{parts[0]}: {value}"); return; }
            int pi = Find(parent, out int pind);
            if (pi < 0) { Log.Warn("config section missing: " + parent); return; }
            int e = pi + 1;
            while (e < lines.Count && (lines[e].Trim().Length == 0 || Indent(lines[e]) > pind)) e++;
            while (e > pi + 1 && lines[e - 1].Trim().Length == 0) e--;
            lines.Insert(e, new string(' ', pind + 2) + parts[parts.Length - 1] + ": " + value);
        }
    }

    // ---------- WinDynamicDesktop: the wallpaper that changes with the sun ----------
    public static class Wallpaper
    {
        public static string Exe => Path.Combine(AppPaths.WddDir, "WinDynamicDesktop.exe");
        public static bool Installed => File.Exists(Exe);
        static string SettingsPath => Path.Combine(AppPaths.WddDir, "settings.json");
        static string ThemesDir => Path.Combine(AppPaths.WddDir, "themes");

        public static string CurrentTheme()
        {
            try
            {
                var s = Json.ParseObject(File.ReadAllText(SettingsPath));
                return s.Arr("activeThemes")?.FirstOrDefault() as string;
            }
            catch { return null; }
        }

        public static string CurrentImage(string theme)
        {
            var dir = Path.Combine(ThemesDir, theme ?? "");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jpg").OrderBy(f => f).Skip(4).FirstOrDefault() ?? Directory.GetFiles(dir, "*.jpg").FirstOrDefault() : null;
        }

        // WinDynamicDesktop's own list of where each built-in theme downloads from (github first, then mirrors).
        const string DefaultThemesUrl = "https://raw.githubusercontent.com/t1m0thyj/WinDynamicDesktop/main/src/resources/default_themes.yaml";

        public static void Apply(string theme, LightDark ld)
        {
            if (string.IsNullOrEmpty(theme)) return;
            if (!Installed) { Log.Warn("WinDynamicDesktop isn't installed"); return; }
            bool have = File.Exists(Path.Combine(ThemesDir, theme, "theme.json"));
            if (!have) Download(theme);
            Dictionary<string, object> s;
            try { s = Json.ParseObject(File.ReadAllText(SettingsPath)); } catch { s = new Dictionary<string, object>(); }
            var before = Json.Write(s);
            s["activeThemes"] = new List<object> { theme };
            if (s.Str("language") == null) s["language"] = "en";   // otherwise its first start asks, in a box whose buttons came out blank
            if (ld != null && ld.CustomTimes)
            {
                s["locationMode"] = -1L;
                s["sunriseTime"] = ld.LightAt + ":00"; s["sunsetTime"] = ld.DarkAt + ":00";
            }
            else if (ld != null && ld.UseWindowsLocation) s["locationMode"] = 1L;
            else if (ld != null && (ld.Lat != 0 || ld.Lon != 0))
            {
                s["locationMode"] = 0L;
                s["location"] = string.IsNullOrEmpty(ld.Place) ? s.Str("location", "") : ld.Place;
                s["latitude"] = ld.Lat; s["longitude"] = ld.Lon;
            }
            if (Json.Write(s) == before && have && Shell.Running("WinDynamicDesktop")) return;
            Shell.StopProcess("WinDynamicDesktop");
            File.WriteAllText(SettingsPath, Json.Write(s), new UTF8Encoding(false));
            // it finds its themes folder from the folder it starts in (started elsewhere it says "Failed to load themes")
            Shell.AsUser(Exe, "", AppPaths.WddDir);
            Log.Info("wallpaper: " + theme);
        }

        public static void Download(string theme, string themesDir = null)
        {
            themesDir = themesDir ?? ThemesDir;
            var yaml = Net.GetText(DefaultThemesUrl).Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
            int at = yaml.IndexOf(theme + ":");
            if (at < 0) throw new InvalidOperationException("WinDynamicDesktop has no theme called " + theme);
            var urls = yaml.Skip(at + 1).TakeWhile(l => l.StartsWith("- ")).Select(l => l.Substring(2)).ToList();
            foreach (var url in urls)
            {
                try
                {
                    Log.Info($"downloading the {theme} wallpaper from {new Uri(url).Host}...");
                    var bytes = Net.Get(url);
                    var dest = Path.Combine(themesDir, theme);
                    var tmp = dest + ".part";
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    using (var z = new ZipArchive(new MemoryStream(bytes)))
                    {
                        if (z.GetEntry("theme.json") == null) throw new InvalidDataException("no theme.json inside");
                        foreach (var e in z.Entries)
                        {
                            if (e.FullName.EndsWith("/")) continue;
                            var f = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                            if (!f.StartsWith(Path.GetFullPath(tmp))) continue;   // ignore paths that try to leave the folder
                            Directory.CreateDirectory(Path.GetDirectoryName(f));
                            e.ExtractToFile(f, true);
                        }
                    }
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    Directory.Move(tmp, dest);
                    Log.Info($"  {theme}: {Directory.GetFiles(dest).Length} files");
                    return;
                }
                catch (Exception e) { Log.Warn($"  {url}: {e.Message}"); }
            }
            throw new InvalidOperationException("couldn't download the " + theme + " wallpaper");
        }
    }

    // ---------- PowerToys: Command Palette and PowerToys Run off (Raycast owns Alt+Space), start at login ----------
    public static class PowerToys
    {
        public static bool Installed => AppPaths.PowerToysExe != null;

        public static void Apply()
        {
            if (!Installed || !File.Exists(AppPaths.PowerToysSettings)) return;
            var text = File.ReadAllText(AppPaths.PowerToysSettings);
            var s = Json.ParseObject(text);
            var en = s.Obj("enabled") ?? new Dictionary<string, object>();
            bool change = en.Bool("CmdPal", true) || en.Bool("PowerToys Run", true) || !s.Bool("startup");
            if (!change) return;
            Shell.StopProcess("PowerToys");
            foreach (var p in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("PowerToys.")).ToList()) try { p.Kill(); } catch { }
            System.Threading.Thread.Sleep(1500);
            s = Json.ParseObject(File.ReadAllText(AppPaths.PowerToysSettings));
            en = s.Obj("enabled") ?? new Dictionary<string, object>();
            en["CmdPal"] = false; en["PowerToys Run"] = false; s["enabled"] = en;
            s["startup"] = true;   // PowerToys registers its own sign-in task from this
            Backup.SaveFile(AppPaths.PowerToysSettings);
            File.WriteAllText(AppPaths.PowerToysSettings, Json.Write(s), new UTF8Encoding(false));
            Shell.AsUser(AppPaths.PowerToysExe, "", null);
            Log.Info("PowerToys: Command Palette and Run off (Alt+Space is Raycast's), starts at sign-in");
        }
    }

    // ---------- macOS-style pointers (github.com/ful1e5/apple_cursor, the Windows pack of release v2.0.1) ----------
    public static class Pointers
    {
        const string ZipUrl = "https://github.com/ful1e5/apple_cursor/releases/download/v2.0.1/macOS-Windows.zip";
        static string Dest => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cursors\macOS");
        static readonly (string role, string file)[] Map =
        {
            ("Arrow", "Pointer.cur"), ("Help", "Help.cur"), ("AppStarting", "Work.ani"), ("Wait", "Busy.ani"), ("Crosshair", "Cross.cur"),
            ("IBeam", "Text.cur"), ("NWPen", "Handwriting.cur"), ("SizeNS", "Vert.cur"), ("SizeWE", "Horz.cur"), ("SizeNWSE", "Dng1.cur"),
            ("SizeNESW", "Dng2.cur"), ("SizeAll", "Move.cur"), ("UpArrow", "Alternate.cur"), ("Hand", "Link.cur"), ("Pin", "Pin.cur"),
            ("Person", "Person.cur"), ("No", "Unavailiable.cur")
        };
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);

        public static bool On => (Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors")?.GetValue("Arrow") as string ?? "").IndexOf(@"\Cursors\macOS\", StringComparison.OrdinalIgnoreCase) >= 0;

        public static void Apply(bool on)
        {
            if (on == On) return;
            if (on)
            {
                if (!File.Exists(Path.Combine(Dest, "Pointer.cur"))) Download();
                Backup.Save(@"HKCU\Control Panel\Cursors");
                using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors"))
                {
                    foreach (var (role, file) in Map) k.SetValue(role, Path.Combine(Dest, file), RegistryValueKind.ExpandString);
                    k.SetValue("", "macOS");
                    k.SetValue("Scheme Source", 1, RegistryValueKind.DWord);
                }
            }
            else
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors"))
                {
                    foreach (var (role, _) in Map) k.SetValue(role, "", RegistryValueKind.ExpandString);   // Windows' own
                    k.SetValue("", "Windows Default");
                    k.SetValue("Scheme Source", 2, RegistryValueKind.DWord);
                }
            }
            SystemParametersInfo(0x57, 0, IntPtr.Zero, 3);   // SPI_SETCURSORS: reload, save and tell everyone
            Log.Info("macOS-style pointers " + (on ? "on" : "off"));
        }

        public static void Download(string dest = null)
        {
            dest = dest ?? Dest;
            Log.Info("downloading the macOS-style pointers from github.com/ful1e5/apple_cursor...");
            var bytes = Net.Get(ZipUrl);
            Directory.CreateDirectory(dest);
            using (var z = new ZipArchive(new MemoryStream(bytes)))
            {
                // the regular-size set (the pack also has large and extra-large ones)
                var pointer = z.Entries.Where(e => e.Name == "Pointer.cur").OrderBy(e => e.FullName.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0).ThenBy(e => e.FullName.Length).FirstOrDefault()
                              ?? throw new InvalidDataException("no pointers in the download");
                var folder = pointer.FullName.Substring(0, pointer.FullName.Length - pointer.Name.Length);
                foreach (var e in z.Entries.Where(e => e.FullName.StartsWith(folder) && e.Name.Length > 0 && !e.FullName.Substring(folder.Length).Contains("/")))
                    if (e.Name.EndsWith(".cur") || e.Name.EndsWith(".ani")) e.ExtractToFile(Path.Combine(dest, e.Name), true);
                Log.Info("  pointers from " + folder);
            }
        }
    }
}
