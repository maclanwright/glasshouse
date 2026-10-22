using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Glasshouse.Ui
{
    // The look being edited (this machine's), the plain-language knobs over its raw mod settings, and the queue that
    // applies each change in the background (mods through the admin helper, the rest as the user).
    public class Editor
    {
        public Look Look;
        public event Action<string> Status;           // "Applying...", "Saved", errors
        public event Action TaskbarRestartNeeded;
        readonly HashSet<string> dirty = new HashSet<string>();
        readonly DispatcherTimer timer;
        bool busy;

        public const string Dock = "macos-style-dock", Island = "mac-island-dock", TaskbarStyler = "windows-11-taskbar-styler",
            Thumbs = "taskbar-thumbnail-size", Start = "windows-11-start-menu-styler", Explorer = "windows-11-file-explorer-styler";

        public Editor(Look look)
        {
            Look = look;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => { timer.Stop(); Flush(); };
        }

        // Loads this machine's look, with the mods' settings as they really are right now.
        public static Look LoadCurrent()
        {
            var look = Repair.WantedLook() ?? Machine.Capture();
            var installed = Windhawk.ReadAll();
            for (int i = 0; i < look.Mods.Count; i++)
            {
                if (!installed.TryGetValue(look.Mods[i].Id, out var have)) continue;
                var spec = have.Spec;
                // personal rules that are off aren't on the machine, but keep them so they can be switched on again
                foreach (var r in PersonalItem.Rules.Where(r => r.Value.mod == spec.Id && !look.Personal.Contains(r.Key)))
                {
                    var st = look.Mods[i].Settings;
                    int at = Settings.FindItem(st, "controlStyles", t => t.Contains(r.Value.match));
                    if (at >= 0 && Settings.FindItem(spec.Settings, "controlStyles", t => t.Contains(r.Value.match)) < 0)
                        Settings.SetRule(spec.Settings, "controlStyles", Settings.Count(spec.Settings, "controlStyles"), (string)st[$"controlStyles[{at}].target"], Settings.RuleStyles(st, "controlStyles", at));
                }
                look.Mods[i] = spec;
            }
            look.Accent = Glasshouse.Accent.Current() ?? look.Accent;
            return look;
        }

        public bool Offline;   // tests: edit the look only
        public void Change(string area, Action<Look> edit)
        {
            edit(Look);
            if (Offline) return;
            dirty.Add(area);
            timer.Stop(); timer.Start();
        }

        async void Flush()
        {
            if (busy) { timer.Start(); return; }
            if (dirty.Count == 0) return;
            busy = true;
            var areas = dirty.ToList(); dirty.Clear();
            var look = Look;
            Status?.Invoke("Applying...");
            string error = null; bool restart = false;
            await Task.Run(() =>
            {
                try { restart = ApplyAreas(look, areas); }
                catch (Exception e) { error = e.Message; Log.Error(e.Message); }
            });
            busy = false;
            Status?.Invoke(error == null ? "Saved" : "Couldn't apply: " + error);
            if (restart) TaskbarRestartNeeded?.Invoke();
            if (dirty.Count > 0) timer.Start();
        }

        // Returns true when the taskbar needs a restart to show the change.
        static bool ApplyAreas(Look look, List<string> areas)
        {
            bool restart = false;
            var personal = look.Personal;
            var mods = look.ModsFor(personal);
            // turning the Dock's auto-hide off happens before its settings change, turning it on after
            if (areas.Contains("autohide") && !look.Tidy.AutoHideDock) Shell.AutoHide = false;
            var modIds = areas.Where(a => a.StartsWith("mod:")).Select(a => a.Substring(4)).ToList();
            if (areas.Contains("autohide") && !modIds.Contains(Dock)) modIds.Add(Dock);
            if (modIds.Count > 0)
            {
                var r = Elevation.Run(new Job { Kind = "windhawk.apply", Mods = mods.Where(m => modIds.Contains(m.Id)).ToList() });
                if (!r.Ok) throw new InvalidOperationException(r.Error);
                if (r.ServicesRestarted) Tailscale.MakeSureItsUp();
                if (r.Changed.Contains(TaskbarStyler)) restart = true;
            }
            if (areas.Contains("autohide") && look.Tidy.AutoHideDock) { System.Threading.Thread.Sleep(1200); Shell.AutoHide = true; }
            if (areas.Contains("accent")) Glasshouse.Accent.Apply(look.Accent);
            if (areas.Contains("lightdark")) AutoDarkMode.Apply(look.LightDark);
            if (areas.Contains("wallpaper")) Wallpaper.Apply(look.Wallpaper, look.LightDark);
            if (areas.Contains("pointers")) Pointers.Apply(look.MacPointers);
            if (areas.Contains("tidy") && Shell.ApplyTidy(look.Tidy)) restart = true;
            if (areas.Contains("phone")) Shell.SetPhonePanelHidden(personal.Contains("phone-panel-hidden"));
            Machine.SaveWanted(look, look.Accent, personal);
            return restart;
        }

        // ---------- knobs: plain names over the mods' raw settings ----------

        public ModSpec M(string id) => Look.Mod(id);
        public long Int(string mod, string key, long def) => M(mod)?.Settings.TryGetValue(key, out var v) == true && !(v is string) ? Convert.ToInt64(v) : def;
        public string Str(string mod, string key, string def) => M(mod)?.Settings.TryGetValue(key, out var v) == true ? Convert.ToString(v) : def;
        public void SetInt(string mod, string key, long value) => Change("mod:" + mod, l => l.Mod(mod).Settings[key] = value);
        public void SetStr(string mod, string key, string value) => Change("mod:" + mod, l => l.Mod(mod).Settings[key] = value);

        // Window previews: one size slider over the thumbnail mod's min/max box (100% = 300x220 to 420x260).
        public double PreviewScale => Int(Thumbs, "minWidth", 300) / 300.0;
        public void SetPreviewScale(double s) => Change("mod:" + Thumbs, l =>
        {
            var st = l.Mod(Thumbs).Settings;
            st["useAbsoluteSize"] = 1L; st["preserveAspectRatio"] = 1L;
            st["minWidth"] = (long)Math.Round(300 * s); st["minHeight"] = (long)Math.Round(220 * s);
            st["maxWidth"] = (long)Math.Round(420 * s); st["maxHeight"] = (long)Math.Round(260 * s);
        });

        // Window previews: how dark the glass box behind them is (the "Fill=#B31C1C1E" rule).
        const string PreviewFillTarget = "Rectangle#BackgroundFill";
        public double PreviewTint
        {
            get
            {
                var st = M(TaskbarStyler)?.Settings; if (st == null) return 0.7;
                int i = Settings.FindItem(st, "controlStyles", t => t.Contains(PreviewFillTarget));
                var fill = i < 0 ? null : Settings.RuleStyles(st, "controlStyles", i).FirstOrDefault(x => x.StartsWith("Fill=#"));
                return fill == null ? 0.7 : Convert.ToInt32(fill.Substring(6, 2), 16) / 255.0;
            }
        }
        public void SetPreviewTint(double a) => Change("mod:" + TaskbarStyler, l =>
        {
            var st = l.Mod(TaskbarStyler).Settings;
            int i = Settings.FindItem(st, "controlStyles", t => t.Contains(PreviewFillTarget));
            if (i < 0) return;
            var styles = Settings.RuleStyles(st, "controlStyles", i);
            for (int j = 0; j < styles.Count; j++)
                if (styles[j].StartsWith("Fill=#") && styles[j].Length >= 14) styles[j] = "Fill=#" + ((int)Math.Round(a * 255)).ToString("X2") + styles[j].Substring(8);
            Settings.SetRule(st, "controlStyles", i, (string)st[$"controlStyles[{i}].target"], styles);
        });

        // Start, search and File Explorer glass: the tint strength of the shared WindhawkBlur constants.
        static readonly Regex TintOpacity = new Regex(@"TintOpacity=""([0-9.]+)""");
        static readonly Regex BlurAmount = new Regex(@"BlurAmount=""([0-9.]+)""");
        public double GlassStrength => ConstValue(Start, "Background=", TintOpacity, 0.2);
        public double GlassBlur => ConstValue(Start, "Background=", BlurAmount, 15);
        double ConstValue(string mod, string prefix, Regex rx, double def)
        {
            var st = M(mod)?.Settings; if (st == null) return def;
            foreach (var kv in st)
                if (kv.Key.StartsWith("styleConstants[") && kv.Value is string s && s.StartsWith(prefix))
                { var m = rx.Match(s); if (m.Success) return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); }
            return def;
        }
        public void SetGlass(double strength, double blur)
        {
            foreach (var mod in new[] { Start, Explorer })
                Change("mod:" + mod, l =>
                {
                    var st = l.Mod(mod)?.Settings; if (st == null) return;
                    foreach (var k in st.Keys.ToList())
                    {
                        if (!k.StartsWith("styleConstants[") || !(st[k] is string s) || !s.Contains("WindhawkBlur")) continue;
                        double mul = s.StartsWith("ElementBackground=") ? 2 : 1;   // the inner panels are twice as strong
                        s = TintOpacity.Replace(s, $"TintOpacity=\"{Math.Min(1, strength * mul).ToString("0.##", CultureInfo.InvariantCulture)}\"");
                        s = BlurAmount.Replace(s, $"BlurAmount=\"{blur.ToString("0", CultureInfo.InvariantCulture)}\"");
                        st[k] = s;
                    }
                });
        }

        // Hover: one soft white everywhere (Start, search, File Explorer, its right-click menu); 7% by default.
        static readonly Regex WhiteAlpha = new Regex(@"#([0-9A-Fa-f]{2})FFFFFF");
        static readonly Regex RgbaWhite = new Regex(@"rgba\(255,\s*255,\s*255,\s*([0-9.]+)\)");
        public double HoverLift
        {
            get
            {
                var st = M(Explorer)?.Settings; if (st == null) return 0.07;
                foreach (var kv in st)
                    if (kv.Key.StartsWith("styleConstants[") && kv.Value is string s && s.StartsWith("AccentBackground="))
                    { var m = WhiteAlpha.Match(s); if (m.Success) return Convert.ToInt32(m.Groups[1].Value, 16) / 255.0; }
                return 0.07;
            }
        }
        public void SetHoverLift(double a)
        {
            string hexA = ((int)Math.Round(a * 255)).ToString("X2");
            foreach (var mod in new[] { Start, Explorer })
                Change("mod:" + mod, l =>
                {
                    var st = l.Mod(mod)?.Settings; if (st == null) return;
                    foreach (var k in st.Keys.ToList())
                    {
                        if (!(st[k] is string s)) continue;
                        bool hoverValue = (k.StartsWith("styleConstants[") && s.StartsWith("AccentBackground="))
                                          || (k.StartsWith("controlStyles[") && k.Contains(".styles[") && (s.Contains("PointerOver") || s.Contains("SubMenuOpened") || s.Contains("SearchBoxHover")));
                        if (hoverValue) st[k] = WhiteAlpha.Replace(s, "#" + hexA + "FFFFFF");
                        if (k.StartsWith("webContentStyles[") && k.Contains(".styles[")) st[k] = RgbaWhite.Replace(s, $"rgba(255,255,255,{a.ToString("0.##", CultureInfo.InvariantCulture)})");
                    }
                });
        }

        // "Keep Windows' own parts dark all day": the stylers' RequestedTheme=2 rule on every Grid.
        public bool DarkShell => (M(Explorer) != null && Settings.FindItem(M(Explorer).Settings, "controlStyles", t => t == "Grid") >= 0);
        public void SetDarkShell(bool on)
        {
            if (on == DarkShell) return;
            foreach (var mod in new[] { Explorer, Start })
                Change("mod:" + mod, l =>
                {
                    var st = l.Mod(mod)?.Settings; if (st == null) return;
                    Settings.RemoveArrayItems(st, "controlStyles", t => t == "Grid");
                    if (on) Settings.SetRule(st, "controlStyles", Settings.Count(st, "controlStyles"), "Grid", new[] { "RequestedTheme=2" });
                });
        }
    }
}
