using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Glasshouse
{
    // Reads this machine into a Look, puts a Look onto this machine, and checks how it's doing.
    public static class Machine
    {
        public static string ThisMachineLook => Path.Combine(Log.Dir, "this-machine" + Look.Extension);

        // Mods in the order people think about them (anything else after, alphabetically).
        static readonly string[] ModOrder =
        {
            "macos-style-dock", "mac-island-dock", "windows-11-taskbar-styler", "taskbar-thumbnail-size",
            "windows-11-start-menu-styler", "windows-11-file-explorer-styler", "spoof-light-dark-theme", "translucent-windows"
        };

        public static Look Capture(string name = null)
        {
            var look = new Look
            {
                Name = name ?? Environment.MachineName + " look",
                SavedFrom = Environment.MachineName,
                SavedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                Accent = Accent.Current() ?? "#F05A22",
                MacPointers = Pointers.On,
                Wallpaper = Wallpaper.CurrentTheme() ?? "",
                Tidy = Shell.ReadTidy()
            };
            look.Mods = Windhawk.ReadAll().Values.Select(i => i.Spec)
                .OrderBy(m => Array.IndexOf(ModOrder, m.Id) is int ix && ix >= 0 ? ix : 99).ThenBy(m => m.Id).ToList();
            var ld = AutoDarkMode.Read();
            if (ld != null)
            {
                try
                {
                    var s = Json.ParseObject(File.ReadAllText(Path.Combine(AppPaths.WddDir, "settings.json")));
                    ld.Place = s.Str("location", "");
                }
                catch { }
                look.LightDark = ld;
            }
            else look.LightDark = new LightDark { Enabled = false };
            look.Apps["raycast"] = AppPaths.RaycastInstalled;
            look.Apps["powertoys"] = PowerToys.Installed;
            look.Apps["fileConverter"] = AppPaths.FileConverterInstalled;
            foreach (var r in PersonalItem.Rules)
            {
                var m = look.Mod(r.Value.mod);
                if (m != null && Settings.FindItem(m.Settings, "controlStyles", t => t.Contains(r.Value.match)) >= 0) look.Personal.Add(r.Key);
            }
            if (Shell.PhonePanelHidden) look.Personal.Add("phone-panel-hidden");
            return look;
        }

        public class Options
        {
            public string Accent;                      // null = the look's own colour
            public ISet<string> PersonalOn;           // null = the look's own (right on the machine it came from)
            public bool Mods = true, LightDark = true, Wallpaper = true, Pointers = true, Tidy = true, Apps = true;
            public bool DryRun, NoRestart;
        }

        public class Result
        {
            public List<string> Changes = new List<string>();
            public bool NeedsTaskbarRestart, RestartedTaskbar, ServicesRestarted;
        }

        // The personal bits that are on by default: all of the look's own on the machine it was saved on,
        // none anywhere else.
        public static ISet<string> DefaultPersonal(Look look) =>
            string.Equals(look.SavedFrom, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ? new HashSet<string>(look.Personal) : new HashSet<string>();

        public static List<Windhawk.ModDiff> Plan(Look look, Options o)
        {
            var personal = o.PersonalOn ?? DefaultPersonal(look);
            var installed = Windhawk.ReadAll();
            return look.ModsFor(personal).Select(m => Windhawk.Diff(m, installed.TryGetValue(m.Id, out var i) ? i : null)).ToList();
        }

        public static Result Apply(Look look, Options o)
        {
            var res = new Result();
            var personal = o.PersonalOn ?? DefaultPersonal(look);
            Log.Info($"applying \"{look.Name}\" (from {look.SavedFrom}){(o.DryRun ? " - DRY RUN, nothing changes" : "")}");
            Log.Info("personal bits on: " + (personal.Count == 0 ? "none" : string.Join(", ", personal)));

            if (o.Mods)
            {
                var mods = look.ModsFor(personal);
                var installed = Windhawk.ReadAll();
                var diffs = mods.Select(m => (m, d: Windhawk.Diff(m, installed.TryGetValue(m.Id, out var i) ? i : null))).ToList();
                foreach (var x in diffs) Log.Info("  " + x.d);
                var changed = diffs.Where(x => x.d.Any).Select(x => x.m).ToList();
                if (!o.DryRun)
                {
                    // the Dock mod owns auto-hide while "magnify over windows" is on, so the order matters:
                    // turning auto-hide off goes first, turning it on goes after the Dock's settings
                    if (o.Tidy && !look.Tidy.AutoHideDock) Shell.AutoHide = false;
                    if (changed.Count > 0)
                    {
                        if (!Windhawk.IsInstalled) throw new InvalidOperationException("Windhawk isn't installed: run Set up first");
                        var r = Elevation.Run(new Job { Kind = "windhawk.apply", Mods = changed });
                        if (!r.Ok) throw new InvalidOperationException("couldn't change the Windhawk mods: " + r.Error);
                        res.Changes.AddRange(r.Changed.Select(c => "mod " + c));
                        res.ServicesRestarted = r.ServicesRestarted;
                        if (r.Changed.Contains("windows-11-taskbar-styler")) res.NeedsTaskbarRestart = true;
                    }
                    if (o.Tidy && look.Tidy.AutoHideDock) { Thread.Sleep(changed.Count > 0 ? 1500 : 0); Shell.AutoHide = true; }
                    Shell.SetPhonePanelHidden(personal.Contains("phone-panel-hidden"));
                    if (res.ServicesRestarted) Tailscale.MakeSureItsUp();
                }
            }
            if (o.DryRun) return res;

            var accent = o.Accent ?? look.Accent;
            if (!Shell.HasDesktop) Log.Warn("no desktop here (a background session): accent colour left as it is");
            else if (!string.IsNullOrEmpty(accent) && !string.Equals(Colors.Normalise(accent), Accent.Current(), StringComparison.OrdinalIgnoreCase))
            { Glasshouse.Accent.Apply(accent); res.Changes.Add("accent " + accent); }
            else if (!string.IsNullOrEmpty(accent)) AutoDarkMode.SetAccent(accent);   // keep Auto Dark Mode's copy in step

            Step("light and dark", o.LightDark && look.LightDark != null && look.LightDark.Enabled, () => AutoDarkMode.Apply(look.LightDark));
            Step("wallpaper", o.Wallpaper, () => Glasshouse.Wallpaper.Apply(look.Wallpaper, look.LightDark));
            Step("pointers", o.Pointers, () => Glasshouse.Pointers.Apply(look.MacPointers));
            Step("Windows settings", o.Tidy, () => { if (Shell.ApplyTidy(look.Tidy)) res.NeedsTaskbarRestart = true; });
            Step("PowerToys", o.Apps && look.Apps.TryGetValue("powertoys", out var pt) && pt, PowerToys.Apply);

            if (res.NeedsTaskbarRestart && !o.NoRestart) { Shell.RestartExplorer(); res.RestartedTaskbar = true; if (o.Tidy && look.Tidy.AutoHideDock) Shell.AutoHide = true; }
            SaveWanted(look, accent, personal);
            Log.Info(res.Changes.Count == 0 ? "done: the look was already in place" : "done: " + string.Join(", ", res.Changes));
            return res;
        }

        static void Step(string what, bool doIt, Action a)
        {
            if (!doIt) return;
            try { a(); }
            catch (Exception e) { Log.Error($"{what}: {e.Message}"); }
        }

        // "This machine's look" is what Repair puts back: the look last applied here, as it landed (this machine's
        // accent and personal bits), not a snapshot of whatever state the machine happens to be in.
        public static void SaveWanted(Look look, string accent, ISet<string> personal)
        {
            try
            {
                var copy = Look.FromJsonText(look.ToJsonText());
                copy.Accent = string.IsNullOrEmpty(accent) ? copy.Accent : Colors.Normalise(accent);
                copy.Personal = new HashSet<string>(personal);
                copy.SavedFrom = Environment.MachineName;
                copy.SavedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
                copy.Save(ThisMachineLook);
            }
            catch (Exception e) { Log.Warn("saving this machine's look: " + e.Message); }
        }

        // ---------- status, for Repair ----------

        public class Check
        {
            public string Name, Detail; public bool Ok;
            public override string ToString() => $"{(Ok ? "OK " : "-- ")} {Name}: {Detail}";
        }

        public static List<Check> Status(Look want)
        {
            var c = new List<Check>();
            c.Add(new Check { Name = "Windhawk", Ok = Windhawk.IsInstalled && Windhawk.ServiceRunning,
                Detail = !Windhawk.IsInstalled ? "not installed" : Windhawk.ServiceRunning ? "running, version " + Windhawk.AppVersion : "installed but not running" });
            if (want != null)
            {
                var diffs = Plan(want, new Options());
                int ok = diffs.Count(d => !d.Any);
                c.Add(new Check { Name = "Mods", Ok = ok == diffs.Count,
                    Detail = ok == diffs.Count ? $"{ok} of {diffs.Count} on, right versions and settings" : $"{ok} of {diffs.Count} right; to fix: " + string.Join("; ", diffs.Where(d => d.Any)) });
            }
            c.Add(new Check { Name = "Smart App Control", Ok = Sac.State != Sac.Mode.On, Detail = Sac.Describe() });
            c.Add(new Check { Name = "Auto Dark Mode", Ok = AutoDarkMode.Running, Detail = !AutoDarkMode.Installed ? "not installed" : AutoDarkMode.Running ? "running" : "installed, not running" });
            var theme = Wallpaper.CurrentTheme();
            c.Add(new Check { Name = "Wallpaper", Ok = Wallpaper.Installed && Shell.Running("WinDynamicDesktop") && !string.IsNullOrEmpty(theme),
                Detail = !Wallpaper.Installed ? "WinDynamicDesktop not installed" : $"{theme ?? "no theme"}, {(Shell.Running("WinDynamicDesktop") ? "running" : "not running")}" });
            c.Add(new Check { Name = "Raycast", Ok = AppPaths.RaycastInstalled, Detail = AppPaths.RaycastInstalled ? (Shell.Running("Raycast") ? "running (Alt+Space)" : "installed") : "not installed" });
            c.Add(new Check { Name = "PowerToys", Ok = PowerToys.Installed, Detail = PowerToys.Installed ? (Shell.Running("PowerToys") ? "running" : "installed") : "not installed" });
            c.Add(new Check { Name = "File Converter", Ok = AppPaths.FileConverterInstalled, Detail = AppPaths.FileConverterInstalled ? "installed" : "not installed" });
            c.Add(new Check { Name = "macOS-style pointers", Ok = Pointers.On, Detail = Pointers.On ? "on" : "off" });
            c.Add(new Check { Name = "Dock auto-hide", Ok = want == null || Shell.AutoHide == want.Tidy.AutoHideDock, Detail = Shell.AutoHide ? "on" : "off" });
            var glass = Windhawk.Read(Windhawk.GlassModId);
            if (glass != null)
            {
                bool safe = Windhawk.SafetyExcludes.All(x => glass.Spec.Exclude.Split('|').Contains(x, StringComparer.OrdinalIgnoreCase));
                c.Add(new Check { Name = "Tailscale and Claude service safety", Ok = safe,
                    Detail = safe ? "window glass skips " + string.Join(" and ", Windhawk.SafetyExcludes) : "window glass could load inside Tailscale: Repair adds the exclusions safely" });
            }
            return c;
        }
    }

    // Smart App Control blocks unsigned code from loading, and that includes Windhawk's mods.
    public static class Sac
    {
        public enum Mode { Off, On, Evaluation, Unknown }
        public static Mode State
        {
            get
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy"))
                {
                    var v = k?.GetValue("VerifiedAndReputablePolicyState");
                    if (v == null) return Mode.Off;
                    switch (Convert.ToInt32(v)) { case 0: return Mode.Off; case 1: return Mode.On; case 2: return Mode.Evaluation; default: return Mode.Unknown; }
                }
            }
        }
        public static string Describe()
        {
            switch (State)
            {
                case Mode.On: return "ON: it blocks Windhawk's mods, so the Dock, menu bar and glass won't load. Turn it off in Windows Security > App and browser control.";
                case Mode.Evaluation: return "in evaluation mode: Windows may switch it on by itself, and then it blocks Windhawk's mods.";
                default: return "off";
            }
        }
    }

    // After a Tailscale service restart the tray app can sit in "NoState" until it's reopened (seen in testing).
    public static class Tailscale
    {
        static string Cli => @"C:\Program Files\Tailscale\tailscale.exe";
        static string Ipn => @"C:\Program Files\Tailscale\tailscale-ipn.exe";

        public static string State()
        {
            if (!File.Exists(Cli)) return null;
            var (code, output) = Shell.Run(Cli, "status --json", 20);
            try { return Json.ParseObject(output).Str("BackendState"); } catch { return code == 0 ? "Running" : "unknown"; }
        }

        public static void MakeSureItsUp()
        {
            if (!File.Exists(Cli)) return;
            for (int i = 0; i < 10; i++) { if (State() == "Running") { Log.Info("Tailscale is up"); return; } Thread.Sleep(2000); }
            Log.Warn("Tailscale is stuck (" + State() + "): reopening its tray app");
            Shell.StopProcess("tailscale-ipn");
            Thread.Sleep(1000);
            if (File.Exists(Ipn)) Shell.AsUser(Ipn, "", null);
            for (int i = 0; i < 15; i++) { Thread.Sleep(2000); if (State() == "Running") { Log.Info("Tailscale is up again"); return; } }
            Log.Error("Tailscale is still not up: " + State());
        }
    }
}
