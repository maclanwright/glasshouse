using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Glasshouse
{
    // --selftest <folder>: downloads every piece of the look from its official source into a scratch folder and
    // compares it with what this machine has. Changes nothing on the machine.
    public static class SelfTest
    {
        // --knobtest: every window control set to the value it shows must leave every mod exactly as it is,
        // and the extremes must still make valid settings.
        public static void Knobs()
        {
            var look = Ui.Editor.LoadCurrent();
            var before = look.Mods.ToDictionary(m => m.Id, m => Json.Write(m.ToJson()));
            var beforeSpecs = look.Mods.ToDictionary(m => m.Id, m => m.Clone());
            var ed = new Ui.Editor(look) { Offline = true };
            ed.SetGlass(ed.GlassStrength, ed.GlassBlur);
            ed.SetHoverLift(ed.HoverLift);
            ed.SetPreviewTint(ed.PreviewTint);
            ed.SetPreviewScale(ed.PreviewScale);
            ed.SetDarkShell(ed.DarkShell);
            int diff = 0;
            foreach (var m in look.Mods)
            {
                var d = Windhawk.Diff(m, new Windhawk.Installed { Spec = Look.FromJsonText(look.ToJsonText()).Mod(m.Id), LibraryPresent = true });
                if (Json.Write(m.ToJson()) != before[m.Id])
                {
                    diff++; Log.Info("CHANGED by a no-op: " + m.Id);
                    foreach (var kv in m.Settings)
                        if (!beforeSpecs[m.Id].Settings.TryGetValue(kv.Key, out var o) || Convert.ToString(o) != Convert.ToString(kv.Value))
                            Log.Info($"   {kv.Key}: '{o}' -> '{kv.Value}'");
                }
            }
            Log.Info(diff == 0 ? "knobs: setting every control to its own value changes nothing" : $"knobs: {diff} mod(s) changed");
            ed.SetGlass(0.6, 40); ed.SetHoverLift(0.25); ed.SetPreviewTint(0); ed.SetPreviewScale(1.5); ed.SetDarkShell(false);
            Log.Info("extremes: " + ed.M(Ui.Editor.Start).Settings["styleConstants[1]"] + " | " + ed.M(Ui.Editor.Explorer).Settings["controlStyles[0].styles[0]"]
                     + " | thumbs " + ed.M(Ui.Editor.Thumbs).Settings["maxWidth"] + " | dark shell " + ed.DarkShell + " | preview tint " + ed.PreviewTint);
            ed.SetDarkShell(true);
            Log.Info("dark shell back on: " + ed.DarkShell + ", explorer rules " + Settings.Count(ed.M(Ui.Editor.Explorer).Settings, "controlStyles"));
        }

        static string Md5(string f) { using (var m = MD5.Create()) using (var s = File.OpenRead(f)) return BitConverter.ToString(m.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }

        public static void Downloads(string dir)
        {
            Directory.CreateDirectory(dir);
            int bad = 0;
            foreach (var m in Windhawk.ReadAll().Values)
            {
                var lib = Windhawk.DownloadAndPlace(m.Spec.Id, m.Spec.Version, Path.Combine(dir, "mods"), Path.Combine(dir, "src"));
                foreach (var arch in new[] { "64", "32" })
                {
                    var got = Path.Combine(dir, "mods", arch, lib);
                    var have = Path.Combine(Windhawk.EngineModsDir, arch, m.LibraryFileName);
                    if (!File.Exists(got)) continue;
                    bool same = File.Exists(have) && Md5(got) == Md5(have);
                    if (!same) bad++;
                    Log.Info($"{(same ? "same" : "DIFFERENT")}  {m.Spec.Id} {m.Spec.Version} {arch}-bit");
                }
            }
            var theme = Wallpaper.CurrentTheme();
            if (!string.IsNullOrEmpty(theme))
            {
                Wallpaper.Download(theme, Path.Combine(dir, "themes"));
                var mine = Path.Combine(AppPaths.WddDir, "themes", theme);
                foreach (var f in Directory.GetFiles(Path.Combine(dir, "themes", theme)))
                {
                    var h = Path.Combine(mine, Path.GetFileName(f));
                    bool same = File.Exists(h) && Md5(h) == Md5(f);
                    if (!same) bad++;
                    Log.Info($"{(same ? "same" : "DIFFERENT")}  wallpaper {theme}/{Path.GetFileName(f)}");
                }
            }
            Pointers.Download(Path.Combine(dir, "pointers"));
            var cur = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cursors\macOS");
            foreach (var f in Directory.GetFiles(Path.Combine(dir, "pointers")))
            {
                var h = Path.Combine(cur, Path.GetFileName(f));
                if (!File.Exists(h)) { Log.Info($"extra     pointer {Path.GetFileName(f)} (not used)"); continue; }
                bool same = Md5(h) == Md5(f);
                if (!same) bad++;
                Log.Info($"{(same ? "same" : "DIFFERENT")}  pointer {Path.GetFileName(f)}");
            }
            Log.Info(bad == 0 ? "selftest: every download matches this machine" : $"selftest: {bad} difference(s)");
        }
    }
}
