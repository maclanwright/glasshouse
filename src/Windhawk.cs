using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace Glasshouse
{
    // Windhawk mods, installed and set the way Windhawk 1.7's own Install button does it (no clicks in its UI):
    // the official prebuilt DLLs from mods.windhawk.net, a registry entry per mod with include/exclude/version,
    // the settings, the source copy and the user-profile version. Settings changes take effect live when
    // SettingsChangeTime is bumped. Writing needs admin (HKLM), reading doesn't.
    public static class Windhawk
    {
        public const string ModsKey = @"SOFTWARE\Windhawk\Engine\Mods";
        public const string GlassModId = "translucent-windows";
        public static readonly string AppDir = @"C:\Program Files\Windhawk";
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Windhawk");
        public static string EngineModsDir => Path.Combine(DataDir, "Engine", "Mods");

        // Services that must never have the window-glass mod loaded: unloading it inside them crashed Tailscale and
        // Claude's cowork service (a test laptop fell off Tailscale twice, Oct 2026). Safety, not taste: always kept.
        public static readonly string[] SafetyExcludes = { "tailscaled.exe", "cowork-svc.exe" };
        // ...and never inside apps that draw with the graphics card: loaded into them while they ran, it blanked Glasshouse's
        // own window and scrambled the text in Claude's app (seen in testing, Oct 2026). Neither gains anything from it.
        public static readonly string[] OwnExcludes = { "Glasshouse.exe", "claude.exe" };
        // Windows' own background services have no windows to restyle; keeping it out of them means a change to it
        // never has to unload inside one (sshd is the way in to a remote machine)
        public static readonly string[] ServiceExcludes = { "svchost.exe", "sshd.exe", "spoolsv.exe", "SearchIndexer.exe", "WmiPrvSE.exe", "MsMpEng.exe" };
        // Design rule: the glass is for everything that comes with Windows and
        // nothing anyone installs. Apps draw their own design, and they were where it glitched.
        public const string WindowsOnly = @"%SystemRoot%\*";

        public static string WithSafetyExcludes(string exclude)
        {
            var parts = (exclude ?? "").Split('|').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            foreach (var s in SafetyExcludes.Concat(OwnExcludes).Concat(ServiceExcludes)) if (!parts.Any(p => p.Equals(s, StringComparison.OrdinalIgnoreCase))) parts.Add(s);
            return string.Join("|", parts);
        }

        public static bool IsInstalled => File.Exists(Path.Combine(AppDir, "windhawk.exe"));
        public static string AppVersion => IsInstalled ? FileVersionInfo.GetVersionInfo(Path.Combine(AppDir, "windhawk.exe")).FileVersion : null;
        public static bool ServiceRunning
        {
            get { try { using (var sc = new ServiceController("Windhawk")) return sc.Status == ServiceControllerStatus.Running; } catch { return false; } }
        }

        // A mod "loads everywhere" when its include list has a wildcard; changing one of those reloads it inside
        // every program, services included. Those changes go through the safe path below.
        public static bool LoadsEverywhere(string include) => (include ?? "").Contains("*");

        public class Installed
        {
            public ModSpec Spec;
            public string LibraryFileName;
            public bool LibraryPresent;
        }

        public static Dictionary<string, Installed> ReadAll()
        {
            var all = new Dictionary<string, Installed>();
            using (var root = Registry.LocalMachine.OpenSubKey(ModsKey))
            {
                if (root == null) return all;
                foreach (var id in root.GetSubKeyNames())
                {
                    var i = Read(id);
                    if (i != null) all[id] = i;
                }
            }
            return all;
        }

        public static Installed Read(string id)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(ModsKey + "\\" + id))
            {
                if (k == null) return null;
                var spec = new ModSpec
                {
                    Id = id,
                    Version = k.GetValue("Version") as string ?? "",
                    Include = k.GetValue("Include") as string ?? "",
                    Exclude = k.GetValue("Exclude") as string ?? "",
                    Architecture = k.GetValue("Architecture") as string ?? "",
                    Disabled = Convert.ToInt32(k.GetValue("Disabled") ?? 0) != 0
                };
                using (var s = k.OpenSubKey("Settings"))
                    if (s != null)
                        foreach (var n in s.GetValueNames())
                        {
                            var v = s.GetValue(n);
                            spec.Settings[n] = s.GetValueKind(n) == RegistryValueKind.DWord ? (object)(long)(int)v : Convert.ToString(v);
                        }
                var lib = k.GetValue("LibraryFileName") as string ?? "";
                return new Installed
                {
                    Spec = spec, LibraryFileName = lib,
                    LibraryPresent = lib.Length > 0 && File.Exists(Path.Combine(EngineModsDir, NativeArchFolder, lib))
                };
            }
        }

        // ---------- what would change (read-only; used for "plan", Repair's status and the window) ----------

        public class ModDiff
        {
            public string Id;
            public bool New, Reinstall, LoadChange, DisabledChange;
            public List<string> Set = new List<string>(), Removed = new List<string>();
            public bool Any => New || Reinstall || LoadChange || DisabledChange || Set.Count > 0 || Removed.Count > 0;
            public override string ToString()
            {
                if (New) return $"{Id}: install";
                var parts = new List<string>();
                if (Reinstall) parts.Add("reinstall");
                if (LoadChange) parts.Add("where it loads");
                if (DisabledChange) parts.Add("on/off");
                if (Set.Count > 0) parts.Add($"{Set.Count} setting(s): {string.Join(", ", Set.Take(6))}{(Set.Count > 6 ? "..." : "")}");
                if (Removed.Count > 0) parts.Add($"{Removed.Count} removed: {string.Join(", ", Removed.Take(6))}{(Removed.Count > 6 ? "..." : "")}");
                return $"{Id}: " + (parts.Count == 0 ? "same" : string.Join("; ", parts));
            }
        }

        public static ModDiff Diff(ModSpec want, Installed have)
        {
            var d = new ModDiff { Id = want.Id };
            if (have == null) { d.New = true; return d; }
            d.Reinstall = have.Spec.Version != want.Version || !have.LibraryPresent || ModFixes.Missing(want, have.LibraryFileName);
            d.LoadChange = have.Spec.Include != want.Include || have.Spec.Exclude != want.Exclude || have.Spec.Architecture != want.Architecture;
            d.DisabledChange = have.Spec.Disabled != want.Disabled;
            foreach (var kv in want.Settings)
                if (!have.Spec.Settings.TryGetValue(kv.Key, out var v) || !SameValue(v, kv.Value)) d.Set.Add(kv.Key);
            foreach (var k in have.Spec.Settings.Keys) if (!want.Settings.ContainsKey(k)) d.Removed.Add(k);
            return d;
        }

        static bool SameValue(object a, object b)
        {
            if (a is string || b is string) return Convert.ToString(a) == Convert.ToString(b) && (a is string) == (b is string);
            return Convert.ToInt64(a) == Convert.ToInt64(b);
        }

        // ---------- applying (admin) ----------

        public class ApplyResult
        {
            public List<string> Changed = new List<string>();
            public bool ExplorerModsChanged, ServicesRestarted;
        }

        public static ApplyResult Apply(IEnumerable<ModSpec> mods)
        {
            if (!IsInstalled) throw new InvalidOperationException("Windhawk isn't installed");
            var res = new ApplyResult();
            EnsureRuntimeFiles();
            foreach (var want in mods)
            {
                if (want.Id == GlassModId) want.Exclude = WithSafetyExcludes(want.Exclude);
                var have = Read(want.Id);
                var diff = Diff(want, have);
                if (!diff.Any) { Log.Info($"  {want.Id} {want.Version}: already right"); continue; }
                Log.Info("  " + diff);
                bool everywhere = LoadsEverywhere(want.Include) || (have != null && LoadsEverywhere(have.Spec.Include));
                if (everywhere && have != null && !have.Spec.Disabled)
                {
                    // The hard-won rule: never reload a load-everywhere mod while the services it could crash are
                    // running. Stop them, change it, give every process time to see it, start them again.
                    // Only services that really have it loaded are at risk: since it loads into Windows' own programs
                    // only, Tailscale and Cowork never have it, so they keep running (no restart, no "log in" pop-up).
                    var svcs = ProtectedServices(want.Id);
                    if (svcs.Count == 0) WriteMod(want, have, diff);
                    else
                    {
                        Log.Info($"  {want.Id} is loaded in {string.Join(", ", svcs)}: stopping them while it changes");
                        StopServices(svcs);
                        try { WriteMod(want, have, diff); System.Threading.Thread.Sleep(8000); }
                        finally { StartServices(svcs); }
                        res.ServicesRestarted = true;
                    }
                }
                else WriteMod(want, have, diff);
                res.Changed.Add(want.Id);
                if (want.Include.Split('|').Any(p => p.Trim().Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))) res.ExplorerModsChanged = true;
            }
            if (res.Changed.Count > 0) UpdateUserProfile(mods.Where(m => res.Changed.Contains(m.Id)));
            return res;
        }

        static void WriteMod(ModSpec want, Installed have, ModDiff diff)
        {
            string lib = have?.LibraryFileName;
            if (diff.New || diff.Reinstall) lib = DownloadAndPlace(want.Id, want.Version);
            using (var k = Registry.LocalMachine.CreateSubKey(ModsKey + "\\" + want.Id))
            {
                bool fresh = have == null;
                // a brand-new mod is written switched off and only switched on once everything (its exclude list
                // above all) is in place, so it never loads anywhere it shouldn't
                if (fresh) k.SetValue("Disabled", 1, RegistryValueKind.DWord);
                k.SetValue("Include", want.Include, RegistryValueKind.String);
                k.SetValue("Exclude", want.Exclude, RegistryValueKind.String);
                k.SetValue("Architecture", want.Architecture, RegistryValueKind.String);
                k.SetValue("Version", want.Version, RegistryValueKind.String);
                using (var s = k.CreateSubKey("Settings"))
                {
                    var cur = s.GetValueNames().ToList();
                    foreach (var n in cur) if (!want.Settings.ContainsKey(n)) s.DeleteValue(n, false);   // names hold [ ], so no wildcards here
                    foreach (var kv in want.Settings)
                    {
                        if (kv.Value is string str) s.SetValue(kv.Key, str, RegistryValueKind.String);
                        else s.SetValue(kv.Key, unchecked((int)Convert.ToInt64(kv.Value)), RegistryValueKind.DWord);
                    }
                }
                if (lib != null) k.SetValue("LibraryFileName", lib, RegistryValueKind.String);
                k.SetValue("SettingsChangeTime", unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()), RegistryValueKind.DWord);
                k.SetValue("Disabled", want.Disabled ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        // Bump only (settings already written elsewhere).
        public static void Touch(string id)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(ModsKey + "\\" + id, true))
                k?.SetValue("SettingsChangeTime", unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()), RegistryValueKind.DWord);
        }

        // ---------- files from mods.windhawk.net ----------

        public static string NativeArchFolder => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "64";

        public static string DownloadAndPlace(string id, string version, string modsDir = null, string sourceDir = null)
        {
            modsDir = modsDir ?? EngineModsDir; sourceDir = sourceDir ?? Path.Combine(DataDir, "ModsSource");
            // a bug the author's build still has: our fixed build of the same version (see ModFixes)
            var fix = ModFixes.For(id, version);
            if (fix != null && ModFixes.Place(fix, modsDir, sourceDir) is string fixedLib) return fixedLib;
            string rand = new Random().Next(100000, 999999).ToString();
            string lib = $"{id}_{version}_{rand}.dll";
            var folders = RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? new[] { ("arm64", "arm64"), ("64", "64"), ("32", "32") } : new[] { ("64", "64"), ("32", "32") };
            int placed = 0;
            foreach (var (folder, suffix) in folders)
            {
                var url = $"https://mods.windhawk.net/mods/{id}/{version}_{suffix}.dll";
                var bytes = Net.TryGet(url);
                if (bytes == null) continue;   // not every mod ships every architecture (most are 64-bit only)
                if (bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("not a DLL: " + url);
                var dir = Path.Combine(modsDir, folder);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, lib), bytes);
                placed++;
                Log.Info($"    downloaded {url} ({bytes.Length / 1024} KB)");
            }
            if (placed == 0) throw new InvalidOperationException($"mods.windhawk.net has no build of {id} {version}");
            var src = Net.TryGet($"https://mods.windhawk.net/mods/{id}/{version}.wh.cpp");
            if (src != null)
            {
                Directory.CreateDirectory(sourceDir);
                File.WriteAllBytes(Path.Combine(sourceDir, id + ".wh.cpp"), src);
            }
            return lib;
        }

        // Prebuilt mods need Windhawk's runtime libraries next to them. Windhawk copies them from its own compiler
        // folder when it installs its first mod; a mod installed without its UI has to do the same.
        public static void EnsureRuntimeFiles()
        {
            var map = new[] { ("64", "x86_64-w64-mingw32"), ("32", "i686-w64-mingw32"), ("arm64", "aarch64-w64-mingw32") };
            foreach (var (folder, tool) in map)
            {
                var bin = Path.Combine(AppDir, "Compiler", tool, "bin");
                if (!Directory.Exists(bin)) continue;
                if (folder == "arm64" && RuntimeInformation.OSArchitecture != Architecture.Arm64) continue;
                var dir = Path.Combine(EngineModsDir, folder);
                Directory.CreateDirectory(dir);
                foreach (var (from, to) in new[] { ("libc++.dll", "libc++.whl"), ("libunwind.dll", "libunwind.whl"), ("windhawk-mod-shim.dll", "windhawk-mod-shim.dll") })
                {
                    var s = Path.Combine(bin, from); var d = Path.Combine(dir, to);
                    if (File.Exists(s) && !File.Exists(d)) { File.Copy(s, d); Log.Info($"    runtime file {folder}\\{to}"); }
                }
            }
        }

        // Windhawk's own app reads installed versions from userprofile.json (to show them and offer updates).
        static void UpdateUserProfile(IEnumerable<ModSpec> mods)
        {
            var path = Path.Combine(DataDir, "userprofile.json");
            Dictionary<string, object> j;
            try { j = File.Exists(path) ? Json.ParseObject(File.ReadAllText(path)) : new Dictionary<string, object>(); }
            catch { j = new Dictionary<string, object>(); }
            var m = j.Obj("mods");
            if (m == null) { m = new Dictionary<string, object>(); j["mods"] = m; }
            foreach (var mod in mods)
            {
                var e = m.Obj(mod.Id);
                string latest = e?.Str("latestVersion");
                m[mod.Id] = new Dictionary<string, object>
                {
                    ["version"] = mod.Version,
                    ["latestVersion"] = latest != null && Versions.Compare(latest, mod.Version) > 0 ? latest : mod.Version
                };
            }
            File.WriteAllText(path, Json.Write(j), new UTF8Encoding(false));
        }

        // ---------- the services the window-glass mod must not be reloaded under ----------

        // With a mod id: only the ones that have that mod's DLL loaded right now (or that can't be looked inside).
        public static List<string> ProtectedServices(string modId = null)
        {
            var names = new List<string>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, PathName, State, ProcessId FROM Win32_Service"))
                    foreach (ManagementObject o in q.Get())
                    {
                        var path = (o["PathName"] as string ?? "").ToLowerInvariant();
                        if (!SafetyExcludes.Any(x => path.Contains(x)) || (o["State"] as string) != "Running") continue;
                        if (modId != null && HasModule((int)(uint)o["ProcessId"], modId) == false) { Log.Info($"    {o["Name"]} doesn't have {modId} loaded: left running"); continue; }
                        names.Add((string)o["Name"]);
                    }
            }
            catch (Exception e) { Log.Warn("service list: " + e.Message); }
            return names;
        }

        // true / false, or null when the process can't be looked inside
        static bool? HasModule(int pid, string modId)
        {
            try { using (var p = Process.GetProcessById(pid)) return p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.StartsWith(modId + "_", StringComparison.OrdinalIgnoreCase)); }
            catch { return null; }
        }

        static void StopServices(List<string> names)
        {
            foreach (var n in names) try { using (var sc = new ServiceController(n)) if (sc.Status != ServiceControllerStatus.Stopped) sc.Stop(); } catch (Exception e) { Log.Warn($"stop {n}: {e.Message}"); }
            foreach (var n in names) try { using (var sc = new ServiceController(n)) sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); } catch { }
            Log.Info("    stopped: " + string.Join(", ", names));
        }

        static void StartServices(List<string> names)
        {
            foreach (var n in names) try { using (var sc = new ServiceController(n)) { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); } } catch (Exception e) { Log.Warn($"start {n}: {e.Message}"); }
            Log.Info("    started again: " + string.Join(", ", names));
        }

        // Which programs currently have a mod's DLL loaded (for checks such as "tailscaled is clean").
        public static List<string> ProcessesWithModule(string modId, IEnumerable<string> processNames)
        {
            var hits = new List<string>();
            foreach (var pn in processNames)
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(pn)))
                    try { if (p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.StartsWith(modId + "_", StringComparison.OrdinalIgnoreCase))) hits.Add(pn); }
                    catch { /* needs admin to look inside a service */ }
            return hits;
        }
    }

    public static class Versions
    {
        public static int Compare(string a, string b)
        {
            var pa = a.Split('.', '-'); var pb = b.Split('.', '-');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                var x = i < pa.Length ? pa[i] : "0"; var y = i < pb.Length ? pb[i] : "0";
                int c = int.TryParse(x, out var xi) && int.TryParse(y, out var yi) ? xi.CompareTo(yi) : string.CompareOrdinal(x, y);
                if (c != 0) return c;
            }
            return 0;
        }
    }

    public static class Net
    {
        static Net() { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }

        class Wc : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                r.Timeout = 120000;
                if (r is HttpWebRequest h) h.UserAgent = "Glasshouse/1.0";
                return r;
            }
        }

        public static byte[] Get(string url) { using (var w = new Wc()) return w.DownloadData(url); }

        public static byte[] TryGet(string url)
        {
            try { return Get(url); }
            catch (WebException e) when (e.Response is HttpWebResponse r && (int)r.StatusCode == 404) { return null; }
        }

        public static string GetText(string url) => Encoding.UTF8.GetString(Get(url));
    }
}
