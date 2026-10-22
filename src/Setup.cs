using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Glasshouse
{
    // Set up: everything the look needs, each from its own official source, then the look itself.
    // Windows asks for admin once (Windhawk, File Converter and Glasshouse's helper are machine-wide);
    // the rest installs as the user.
    public static class Setup
    {
        public class Part
        {
            public string Id, Name, What, Source; public Func<bool> Installed; public bool Wanted = true;
            public override string ToString() => $"{(Installed() ? "installed  " : "will install")}  {Name}  ({Source})";
        }

        public static List<Part> Parts(Look look) => new List<Part>
        {
            new Part { Id = "windhawk", Name = "Dock, menu bar and glass", What = $"Windhawk and {look.Mods.Count} of its mods", Source = "windhawk.net, mods.windhawk.net",
                       Installed = () => Windhawk.IsInstalled && Machine.Plan(look, new Machine.Options()).All(d => !d.New && !d.Reinstall) },
            new Part { Id = "autoDarkMode", Name = "Light and dark with the sun", What = "Auto Dark Mode", Source = "winget: ArminOsaj.AutoDarkMode", Installed = () => AutoDarkMode.Installed, Wanted = look.LightDark?.Enabled ?? true },
            new Part { Id = "wallpaper", Name = "Moving wallpaper", What = $"WinDynamicDesktop{(string.IsNullOrEmpty(look.Wallpaper) ? "" : " with " + look.Wallpaper.Replace('_', ' '))}", Source = "winget, then WinDynamicDesktop's own theme download",
                       Installed = () => Wallpaper.Installed && (string.IsNullOrEmpty(look.Wallpaper) || Directory.Exists(Path.Combine(AppPaths.WddDir, "themes", look.Wallpaper))), Wanted = !string.IsNullOrEmpty(look.Wallpaper) },
            new Part { Id = "raycast", Name = "Spotlight search (Alt+Space)", What = "Raycast", Source = "Microsoft Store", Installed = () => AppPaths.RaycastInstalled, Wanted = look.Apps.TryGetValue("raycast", out var r) && r },
            new Part { Id = "powertoys", Name = "Quick Look and window tools", What = "PowerToys (Peek with Ctrl+Space)", Source = "winget: Microsoft.PowerToys", Installed = () => PowerToys.Installed, Wanted = look.Apps.TryGetValue("powertoys", out var p) && p },
            new Part { Id = "fileConverter", Name = "Convert files from the right-click menu", What = "File Converter", Source = "winget: AdrienAllard.FileConverter", Installed = () => AppPaths.FileConverterInstalled, Wanted = look.Apps.TryGetValue("fileConverter", out var f) && f },
            new Part { Id = "pointers", Name = "macOS-style pointers", What = "apple_cursor", Source = "github.com/ful1e5/apple_cursor", Installed = () => Pointers.On, Wanted = look.MacPointers },
        };

        // Set up and its helper task are in place (Windhawk itself may still be missing if it was left out).
        public static bool Done => Elevation.HelperReady;

        // progress(stepId, state, detail): state is doing | done | failed | skipped. Step ids are the part ids,
        // plus "admin" (Windows' admin prompt) and "look" (putting the look in place).
        public static void Run(Look look, Machine.Options o, ISet<string> skip = null, Action<string, string, string> progress = null)
        {
            skip = skip ?? new HashSet<string>();
            progress = progress ?? ((a, b, c) => { });
            var parts = Parts(look).Where(p => p.Wanted && !skip.Contains(p.Id)).ToList();
            bool Has(string id) => parts.Any(p => p.Id == id);
            Log.Info($"Set up Glasshouse on {Environment.MachineName} with \"{look.Name}\"");
            foreach (var p in parts) Log.Info("  " + p);
            if (Sac.State == Sac.Mode.On) Log.Warn("Smart App Control is " + Sac.Describe());
            o.Mods &= Has("windhawk"); o.LightDark &= Has("autoDarkMode"); o.Wallpaper &= Has("wallpaper"); o.Pointers &= Has("pointers");
            look.Apps["powertoys"] = Has("powertoys");
            if (o.DryRun) { Machine.Apply(look, o); return; }
            if (!Winget.Available) throw new InvalidOperationException("winget (App Installer) is missing: install \"App Installer\" from the Microsoft Store first");

            // 1. the admin part, one prompt
            progress("admin", "doing", "Windows asks for permission once");
            foreach (var id in new[] { "windhawk", "fileConverter" }) if (Has(id)) progress(id, "doing", "");
            var job = new Job
            {
                Kind = "setup.admin", InstallSelf = true,
                InstallWindhawk = Has("windhawk"),
                InstallFileConverter = Has("fileConverter") && !AppPaths.FileConverterInstalled,
            };
            var r = Elevation.Run(job);
            if (!r.Ok) { progress("admin", "failed", r.Error); throw new InvalidOperationException("the admin part of setup didn't finish: " + r.Error); }
            progress("admin", "done", "");
            if (Has("windhawk")) progress("windhawk", Windhawk.IsInstalled ? "done" : "failed", Windhawk.IsInstalled ? "Windhawk " + Windhawk.AppVersion : "Windhawk didn't install");
            if (Has("fileConverter")) progress("fileConverter", AppPaths.FileConverterInstalled ? "done" : "failed", "");

            // 2. the user's own apps
            void App(string id, Func<bool> installed, Func<bool> install)
            {
                if (!Has(id)) { progress(id, "skipped", ""); return; }
                if (installed()) { progress(id, "done", "already installed"); return; }
                progress(id, "doing", "downloading");
                bool ok = false;
                try { ok = install(); } catch (Exception e) { Log.Error(e.Message); }
                progress(id, ok && installed() ? "done" : "failed", ok ? "" : "see the log");
            }
            App("autoDarkMode", () => AutoDarkMode.Installed, () => Winget.Install("ArminOsaj.AutoDarkMode"));
            App("wallpaper", () => Wallpaper.Installed, () => Winget.Install("t1m0thyj.WinDynamicDesktop"));
            App("powertoys", () => PowerToys.Installed, () => Winget.Install("Microsoft.PowerToys", scope: "user"));
            App("raycast", () => AppPaths.RaycastInstalled, () => Winget.Install("9PFXXSHC64H3", source: "msstore"));
            RegisterFileType();

            // 3. the look itself (Windhawk changes now go through the helper task: no second prompt)
            progress("look", "doing", "Dock, menu bar, glass, colour, wallpaper and pointers");
            if (Has("pointers")) progress("pointers", "doing", "");
            Machine.Apply(look, o);
            if (Has("pointers")) progress("pointers", Pointers.On ? "done" : "failed", "");
            StartApps(parts);
            progress("look", "done", "");
            Log.Info("Set up finished");
        }

        static void StartApps(List<Part> parts)
        {
            try
            {
                if (parts.Any(p => p.Id == "autoDarkMode") && AutoDarkMode.Installed && !AutoDarkMode.Running)
                {
                    var exe = Path.Combine(AppPaths.AdmDir, @"adm-app\core\AutoDarkModeSvc.exe");
                    Shell.AsUser(exe, "", Path.GetDirectoryName(exe));
                }
                if (parts.Any(p => p.Id == "powertoys") && PowerToys.Installed && !Shell.Running("PowerToys")) Shell.AsUser(AppPaths.PowerToysExe, "", null);
                if (parts.Any(p => p.Id == "raycast") && AppPaths.RaycastInstalled && !Shell.Running("Raycast"))
                    Shell.AsUser("explorer.exe", @"shell:AppsFolder\Raycast.Raycast_qypenmj9wpt2a!Raycast", null);
            }
            catch (Exception e) { Log.Warn("starting apps: " + e.Message); }
        }

        // Double-clicking a .glasshouse file opens it in Glasshouse.
        public static void RegisterFileType()
        {
            var exe = File.Exists(Elevation.InstalledExe) ? Elevation.InstalledExe : Program.SelfPath;
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.glasshouse")) k.SetValue("", "Glasshouse.Look");
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Glasshouse.Look"))
            {
                k.SetValue("", "Glasshouse file");
                using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
                using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", $"\"{exe}\" \"%1\"");
            }
        }
    }
}
