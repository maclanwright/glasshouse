using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

namespace Glasshouse
{
    // "Apps that switch with it". Many apps have their own light and dark. Left on Dark (or Light) they don't move when
    // Auto Dark Mode switches Windows' apps at sunrise and sunset, and they clash: a browser kept on Dark by day draws
    // form text light over pages that paint white boxes, so typed words and dropdown lists go white on white.
    // Each app keeps its own light and dark; Glasshouse only sets it to follow Windows, so everything switches together.
    //  - Automatic: Glasshouse writes the app's own "follow Windows" setting and keeps the old value to put back.
    //  - One step: the app keeps the setting where nothing else can write it (an account, an encrypted store), so the
    //    row says where to click, once.
    //  - No setting: always dark (or no light/dark to follow), shown so people know why they aren't in the list.
    public abstract class FollowApp
    {
        public string Id, Name;
        public string Where;                       // one step: where to click in the app
        public string FollowName;                  // the app's own words for "follow Windows"
        public abstract bool Installed { get; }
        public virtual bool Automatic => false;
        public virtual bool NoSetting => false;
        public virtual string[] Processes => new string[0];
        public virtual bool WaitsForClose => false;   // the app rewrites the setting while it runs: write only while it's closed
        public bool Running => Processes.Any(p => Process.GetProcessesByName(p).Length > 0);

        // Automatic apps: the values now (null = not set), and what has to change for the app to follow Windows.
        public virtual Dictionary<string, string> Read() => new Dictionary<string, string>();
        public virtual Dictionary<string, string> Changes(Dictionary<string, string> now) => new Dictionary<string, string>();
        public virtual void Write(Dictionary<string, string> values) { }
        public bool Follows => Automatic && Changes(Read()).Count == 0;

        public virtual string Exe => null;          // for its icon and the Open button
        public virtual string Logo => null;         // a Store app's picture (PNG)
        public virtual void Open() { if (Exe != null) Shell.AsUser(Exe, "", null); }
    }

    public static class FollowApps
    {
        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string PF = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        static readonly string PF86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // In the order the "Add an app" sheet shows them.
        public static readonly List<FollowApp> All = new List<FollowApp>
        {
            new Chromium("brave", "Brave", "Same as Windows", "brave", Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data"),
                Path.Combine(PF, @"BraveSoftware\Brave-Browser\Application\brave.exe"), Path.Combine(Local, @"BraveSoftware\Brave-Browser\Application\brave.exe")),
            new Chromium("chrome", "Google Chrome", "Device", "chrome", Path.Combine(Local, @"Google\Chrome\User Data"),
                Path.Combine(PF, @"Google\Chrome\Application\chrome.exe"), Path.Combine(PF86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(Local, @"Google\Chrome\Application\chrome.exe")),
            new Office(),
            new CodeEditor("vscode", "Visual Studio Code", "Code", Path.Combine(Roaming, @"Code\User\settings.json"),
                Path.Combine(Local, @"Programs\Microsoft VS Code\Code.exe"), Path.Combine(PF, @"Microsoft VS Code\Code.exe")),
            new CodeEditor("cursor", "Cursor", "Cursor", Path.Combine(Roaming, @"Cursor\User\settings.json"),
                Path.Combine(Local, @"Programs\cursor\Cursor.exe"), Path.Combine(PF, @"cursor\Cursor.exe")),
            new JsonSetting("terminal", "Windows Terminal", "Use Windows theme", "WindowsTerminal", false, "theme", "\"system\"", absentFollows: true,
                file: () => Path.Combine(Local, @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json"), store: "Microsoft.WindowsTerminal"),
            new JsonSetting("powertoys", "PowerToys", "Windows default", "PowerToys", true, "theme", "\"system\"", absentFollows: true,
                file: () => Path.Combine(Local, @"Microsoft\PowerToys\settings.json"), exes: new[] { Path.Combine(PF, @"PowerToys\PowerToys.exe"), Path.Combine(Local, @"PowerToys\PowerToys.exe") }),

            new Step("edge", "Microsoft Edge", "Settings > Appearance > Overall appearance > \"System default\"", "msedge",
                Path.Combine(PF86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(PF, @"Microsoft\Edge\Application\msedge.exe")),
            new Step("discord", "Discord", "User Settings > Appearance > Theme > \"Sync with computer\"", "Discord", Path.Combine(Local, @"Discord\Update.exe")) { Args = "--processStart Discord.exe", IconFrom = () => Newest(Path.Combine(Local, "Discord"), "app-*", "Discord.exe") },
            new Step("whatsapp", "WhatsApp", "Settings > Chats > Theme > \"Same as system\"", "WhatsApp") { Store = "5319275A.WhatsAppDesktop" },
            new Step("telegram", "Telegram", "Settings > Chat Settings > Auto-night mode > \"System\" (it shows while Windows is dark)", "Telegram",
                Path.Combine(Roaming, @"Telegram Desktop\Telegram.exe")) { Store = "TelegramMessengerLLP.TelegramDesktop" },
            new Step("slack", "Slack", "Preferences > Appearance > Colour mode > \"System\"", "slack", Path.Combine(Local, @"slack\slack.exe")) { IconFrom = () => Newest(Path.Combine(Local, "slack"), "app-*", "slack.exe") },
            new Step("zoom", "Zoom", "Settings > General > Appearance > \"Use System Settings\"", "Zoom", Path.Combine(Roaming, @"Zoom\bin\Zoom.exe"), Path.Combine(PF, @"Zoom\bin\Zoom.exe")),
            new Step("figma", "Figma", "Main menu > Preferences > Theme > the system theme", "Figma", Path.Combine(Local, @"Figma\Figma.exe")),
            new Step("bitwarden", "Bitwarden", "File > Settings > Theme > \"Default\" (that one follows Windows)", "Bitwarden",
                Path.Combine(Local, @"Programs\Bitwarden\Bitwarden.exe"), Path.Combine(PF, @"Bitwarden\Bitwarden.exe")),
            new Step("raycast", "Raycast", "Settings > General > Appearance > \"Follow System Appearance\"", "Raycast") { Store = "Raycast.Raycast" },
            new Step("qbittorrent", "qBittorrent", "Tools > Options > Behavior > Color scheme > \"System\", then restart it", "qbittorrent",
                Path.Combine(PF, @"qBittorrent\qbittorrent.exe")),

            new Step("spotify", "Spotify", null, "Spotify", Path.Combine(Roaming, @"Spotify\Spotify.exe")) { None = true },
            new Step("steam", "Steam", null, "steam", Path.Combine(PF86, @"Steam\steam.exe")) { None = true },
            new Step("plex", "Plex", null, "Plex", Path.Combine(PF, @"Plex\Plex\Plex.exe"), Path.Combine(Local, @"Programs\plex\Plex.exe")) { None = true },
            new Step("parsec", "Parsec", null, "parsecd", Path.Combine(PF, @"Parsec\parsecd.exe")) { None = true },
        };

        public static FollowApp Find(string id) => All.FirstOrDefault(a => a.Id == id);

        static string Newest(string dir, string pattern, string exe)
        {
            try { return Directory.GetDirectories(dir, pattern).OrderBy(d => d).Select(d => Path.Combine(d, exe)).LastOrDefault(File.Exists); }
            catch { return null; }
        }

        // ---------- what Glasshouse changed, so it can be put back ----------
        // %LOCALAPPDATA%\Glasshouse\follow-apps.json: {"brave": {"on": true, "before": {"Profile 5": "2"}}, ...}
        // Every app in the list has an entry (so a captured look knows the list); "before" holds the values Glasshouse
        // replaced (null = wasn't set).
        static string StatePath => Path.Combine(Log.Dir, "follow-apps.json");
        static readonly string GateName = @"Local\GlasshouseFollowAppsState";

        class Entry { public bool On; public Dictionary<string, string> Before = new Dictionary<string, string>(); }

        static Dictionary<string, Entry> LoadState()
        {
            var s = new Dictionary<string, Entry>();
            try
            {
                if (!File.Exists(StatePath)) return s;
                foreach (var kv in Json.ParseObject(File.ReadAllText(StatePath, Encoding.UTF8)))
                {
                    var d = kv.Value as Dictionary<string, object>; if (d == null) continue;
                    var e = new Entry { On = d.Bool("on") };
                    var b = d.Obj("before");
                    if (b != null) foreach (var x in b) e.Before[x.Key] = x.Value as string;
                    s[kv.Key] = e;
                }
            }
            catch (Exception e) { Log.Warn("reading " + StatePath + ": " + e.Message); }
            return s;
        }

        static void SaveState(Dictionary<string, Entry> s)
        {
            Directory.CreateDirectory(Log.Dir);
            var d = s.ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object>
            {
                ["on"] = kv.Value.On,
                ["before"] = kv.Value.Before.ToDictionary(b => b.Key, b => (object)b.Value)
            });
            File.WriteAllText(StatePath, Json.Write(d) + "\n", new UTF8Encoding(false));
        }

        static T Locked<T>(Func<T> f)
        {
            using (var m = new Mutex(false, GateName))
            {
                bool got = false;
                try { got = m.WaitOne(15000); } catch (AbandonedMutexException) { got = true; }
                try { return f(); }
                finally { if (got) m.ReleaseMutex(); }
            }
        }

        // The list as this machine has it (for a captured look).
        public static Dictionary<string, bool> Listed() => Locked(() => LoadState().ToDictionary(kv => kv.Key, kv => kv.Value.On));

        // Puts the look's list onto this machine: on = follow Windows, off (or gone from the list) = put back what was
        // there. Apps that are open and rewrite their settings wait: a hidden Glasshouse waits for them to close.
        // Returns the names still waiting.
        public static List<string> Apply(Dictionary<string, bool> want)
        {
            var waiting = Locked(() =>
            {
                var state = LoadState();
                var w = new List<string>();
                foreach (var id in state.Keys.Where(k => !want.ContainsKey(k)).ToList())
                {
                    state[id].On = false;
                    var app = Find(id);
                    if (app != null && !StepOne(app, state[id])) w.Add(app.Name);
                    if (state[id].Before.Count == 0) state.Remove(id);
                }
                foreach (var kv in want)
                {
                    var app = Find(kv.Key);
                    if (app == null) continue;
                    if (!state.TryGetValue(kv.Key, out var e)) state[kv.Key] = e = new Entry();
                    e.On = kv.Value;
                    if (!StepOne(app, e)) w.Add(app.Name);
                }
                SaveState(state);
                return w;
            });
            if (waiting.Count > 0) StartWatcher(); else SignInWatcher(false);
            return waiting;
        }

        // Returns false when the app has to be closed first.
        static bool StepOne(FollowApp app, Entry e)
        {
            if (!app.Automatic || !app.Installed) return true;
            try
            {
                if (e.On)
                {
                    var now = app.Read();
                    var ch = app.Changes(now);
                    if (ch.Count == 0) return true;
                    if (app.WaitsForClose && app.Running) return false;
                    foreach (var k in ch.Keys) if (!e.Before.ContainsKey(k)) e.Before[k] = now.TryGetValue(k, out var v) ? v : null;
                    app.Write(ch);
                    Log.Info($"{app.Name} now follows Windows (\"{app.FollowName}\")");
                }
                else
                {
                    if (e.Before.Count == 0) return true;
                    if (app.WaitsForClose && app.Running) return false;
                    app.Write(e.Before);
                    e.Before.Clear();
                    Log.Info($"{app.Name}: its own light/dark setting is back");
                }
            }
            catch (Exception x) { Log.Error($"{app.Name}: {x.Message}"); }
            return true;
        }

        // What the row says: waiting for the app to close (to switch it on or to put it back)?
        public static bool Waiting(string id) => Locked(() =>
        {
            var app = Find(id);
            if (app == null || !app.Automatic || !app.Installed || !LoadState().TryGetValue(id, out var e)) return false;
            if (e.On) return !app.Follows;
            return e.Before.Count > 0;
        });

        // ---------- the hidden helper that waits for apps to close ----------
        const string RunValue = "Glasshouse follow apps";

        static void StartWatcher()
        {
            SignInWatcher(true);   // also at sign-in, in case the computer restarts first
            // through Explorer, so it keeps going after whatever started it (the window, a command) has finished
            try { Shell.AsUser(Program.SelfPath, "--follow-apps", null); }
            catch (Exception e) { Log.Warn("couldn't start the helper that waits for apps to close: " + e.Message); }
        }

        static void SignInWatcher(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (on) k.SetValue(RunValue, "\"" + Program.SelfPath + "\" --follow-apps");
                    else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue, false);
                }
            }
            catch (Exception e) { Log.Warn("sign-in helper: " + e.Message); }
        }

        // --follow-apps: wait (one copy at a time) until each waiting app has been closed for two checks in a row,
        // set it, and stop when nothing is left.
        public static void Watch()
        {
            using (var single = new Mutex(false, @"Local\GlasshouseFollowAppsWatcher"))
            {
                try { if (!single.WaitOne(0)) return; } catch (AbandonedMutexException) { }
                var closedOnce = new HashSet<string>();
                Log.Info("waiting for apps to close so they can follow Windows");
                while (true)
                {
                    var left = Locked(() =>
                    {
                        var state = LoadState();
                        var l = new List<string>();
                        foreach (var kv in state.ToList())
                        {
                            var app = Find(kv.Key);
                            // only apps that rewrite their setting while open wait here; the rest were set straight away (and an app that
                            // syncs its setting back from an account must not get into a tug of war with this loop)
                            if (app == null || !app.Automatic || !app.Installed || !app.WaitsForClose) continue;
                            bool needs = kv.Value.On ? !app.Follows : kv.Value.Before.Count > 0;
                            if (!needs) { closedOnce.Remove(app.Id); continue; }
                            if (app.Running) { closedOnce.Remove(app.Id); l.Add(app.Name); continue; }
                            if (!closedOnce.Add(app.Id)) { StepOne(app, kv.Value); if (!kv.Value.On && kv.Value.Before.Count == 0) state.Remove(kv.Key); closedOnce.Remove(app.Id); }
                            else l.Add(app.Name);
                        }
                        SaveState(state);
                        return l;
                    });
                    if (left.Count == 0) break;
                    Thread.Sleep(2000);
                }
                SignInWatcher(false);
                Log.Info("every app in the list follows Windows");
            }
        }

        // For Repair and --follow-status.
        public static List<string> Describe(Dictionary<string, bool> want)
        {
            var l = new List<string>();
            foreach (var kv in want)
            {
                var app = Find(kv.Key);
                if (app == null) { l.Add(kv.Key + ": unknown app"); continue; }
                if (!app.Installed) { l.Add(app.Name + ": not on this computer"); continue; }
                if (!app.Automatic) { l.Add(app.Name + ": one step in the app: " + app.Where); continue; }
                bool f = app.Follows;
                l.Add(app.Name + ": " + (kv.Value ? (f ? "follows Windows" : app.Running && app.WaitsForClose ? "waits for it to close" : "doesn't follow Windows yet")
                                                   : "off (keeps its own setting)"));
            }
            return l;
        }

        // ---------- Store apps ----------
        public class Package { public string Family, Root, AppId, Logo; }
        static readonly Dictionary<string, Package> PackageCache = new Dictionary<string, Package>();

        public static Package FindPackage(string name)
        {
            lock (PackageCache)
            {
                if (PackageCache.TryGetValue(name, out var cached)) return cached;
                Package p = null;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                    {
                        var full = k?.GetSubKeyNames().Where(n => n.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) && n.Contains("__") && !n.Contains("_split.") && !n.Contains("~"))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).LastOrDefault();
                        if (full != null)
                            using (var s = k.OpenSubKey(full))
                            {
                                var root = s?.GetValue("PackageRootFolder") as string;
                                if (root != null && Directory.Exists(root))
                                {
                                    p = new Package { Family = name + "_" + full.Substring(full.LastIndexOf('_') + 1), Root = root };
                                    ReadManifest(p);
                                }
                            }
                    }
                }
                catch (Exception e) { Log.Warn("Store app " + name + ": " + e.Message); }
                PackageCache[name] = p;
                return p;
            }
        }

        static void ReadManifest(Package p)
        {
            var doc = new XmlDocument();
            doc.Load(Path.Combine(p.Root, "AppxManifest.xml"));
            var app = doc.GetElementsByTagName("Application").OfType<XmlElement>().FirstOrDefault();
            p.AppId = app?.GetAttribute("Id");
            var ve = app?.GetElementsByTagName("*").OfType<XmlElement>().FirstOrDefault(x => x.LocalName == "VisualElements");
            var logo = ve?.GetAttribute("Square44x44Logo");
            if (string.IsNullOrEmpty(logo)) return;
            var dir = Path.Combine(p.Root, Path.GetDirectoryName(logo));
            var stem = Path.GetFileNameWithoutExtension(logo);
            if (!Directory.Exists(dir)) return;
            // the plain coloured picture: no high-contrast or light-taskbar copies, the biggest of the rest
            p.Logo = Directory.GetFiles(dir, stem + "*.png")
                .Where(f => { var n = Path.GetFileName(f).ToLowerInvariant(); return !n.Contains("contrast") && !n.Contains("lightunplated") && !n.Contains("_light"); })
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        // ---------- the kinds of app ----------

        // A one-step app (or one with no setting to follow): Glasshouse only finds it, shows it and opens it.
        public class Step : FollowApp
        {
            readonly string[] exes; readonly string process;
            public string Store, Args; public Func<string> IconFrom; public bool None;
            public Step(string id, string name, string where, string process, params string[] exes) { Id = id; Name = name; Where = where; this.process = process; this.exes = exes; }
            public override bool NoSetting => None;
            public override string[] Processes => new[] { process };
            Package Pkg => Store == null ? null : FindPackage(Store);
            string FoundExe => exes.FirstOrDefault(File.Exists);
            public override bool Installed => FoundExe != null || Pkg != null;
            public override string Exe => IconFrom?.Invoke() ?? FoundExe;
            public override string Logo => FoundExe == null ? Pkg?.Logo : null;
            public override void Open()
            {
                var exe = FoundExe;
                if (exe != null) { Shell.AsUser(exe, Args, null); return; }
                var p = Pkg;
                if (p?.AppId != null) Shell.AsUser("explorer.exe", $"shell:AppsFolder\\{p.Family}!{p.AppId}", null);
            }
        }

        // Chromium browsers: "browser.theme.color_scheme2" in every profile's Preferences (0 follows Windows, 1 light,
        // 2 dark; not set = follows Windows). The browser rewrites that file while it runs, so only while it's closed.
        public class Chromium : FollowApp
        {
            readonly string userData, process; readonly string[] exes;
            static readonly Regex Key = new Regex("\"color_scheme2\"\\s*:\\s*(\\d+)");
            public Chromium(string id, string name, string followName, string process, string userData, params string[] exes)
            { Id = id; Name = name; FollowName = followName; this.process = process; this.userData = userData; this.exes = exes; }
            public override bool Automatic => true;
            public override bool WaitsForClose => true;
            public override string[] Processes => new[] { process };
            public override bool Installed => exes.Any(File.Exists) && Directory.Exists(userData);
            public override string Exe => exes.FirstOrDefault(File.Exists);

            IEnumerable<string> Profiles() => !Directory.Exists(userData) ? Enumerable.Empty<string>() :
                Directory.GetDirectories(userData).Where(d => !Path.GetFileName(d).Equals("System Profile", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "Preferences")));

            public override Dictionary<string, string> Read()
            {
                var d = new Dictionary<string, string>();
                foreach (var p in Profiles())
                {
                    try
                    {
                        var ms = Key.Matches(File.ReadAllText(Path.Combine(p, "Preferences"), Encoding.UTF8));
                        d[Path.GetFileName(p)] = ms.Count == 1 ? ms[0].Groups[1].Value : null;   // more than one: unsure which, leave it
                    }
                    catch (IOException) { }
                }
                return d;
            }

            public override Dictionary<string, string> Changes(Dictionary<string, string> now) =>
                now.Where(kv => kv.Value != null && kv.Value != "0").ToDictionary(kv => kv.Key, kv => "0");

            public override void Write(Dictionary<string, string> values)
            {
                foreach (var kv in values)
                {
                    var f = Path.Combine(userData, kv.Key, "Preferences");
                    if (!File.Exists(f)) continue;
                    var text = File.ReadAllText(f, Encoding.UTF8);
                    var ms = Key.Matches(text);
                    var want = kv.Value ?? "0";   // not set and 0 mean the same
                    if (ms.Count != 1) { if (want != "0") Log.Warn($"{Name} {kv.Key}: its light/dark setting moved, left as it is"); continue; }
                    if (ms[0].Groups[1].Value == want) continue;
                    var g = ms[0].Groups[1];
                    var after = text.Substring(0, g.Index) + want + text.Substring(g.Index + g.Length);
                    BackupOnce(f, Id + "-" + kv.Key.Replace(' ', '_') + "-Preferences.orig");
                    var tmp = f + ".glasshouse";
                    File.WriteAllText(tmp, after, new UTF8Encoding(false));
                    File.Replace(tmp, f, null);
                    Log.Info($"{Name} {kv.Key}: light/dark setting {g.Value} -> {want}");
                }
            }
        }

        // Office (Word, Excel, PowerPoint, classic Outlook): "UI Theme" 6 = "Use system setting". Signed-in Office also
        // keeps a roaming copy per account (setting 1186), which it applies at start, so that one is set too.
        public class Office : FollowApp
        {
            const string Common = @"Software\Microsoft\Office\16.0\Common";
            const string Ids = Common + @"\Roaming\Identities";
            static readonly string[] Exes =
            {
                Path.Combine(PF, @"Microsoft Office\root\Office16\WINWORD.EXE"), Path.Combine(PF86, @"Microsoft Office\root\Office16\WINWORD.EXE"),
                Path.Combine(PF, @"Microsoft Office\root\Office16\OUTLOOK.EXE"), Path.Combine(PF86, @"Microsoft Office\root\Office16\OUTLOOK.EXE"),
                Path.Combine(PF, @"Microsoft Office\root\Office16\EXCEL.EXE")
            };
            public Office() { Id = "office"; Name = "Microsoft Office"; FollowName = "Use system setting"; }
            public override bool Automatic => true;
            public override bool Installed => Exes.Any(File.Exists);
            public override string Exe => Exes.FirstOrDefault(File.Exists);
            public override void Open() { }

            static IEnumerable<string> RoamingKeys()
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Ids))
                    foreach (var id in k?.GetSubKeyNames() ?? new string[0])
                        using (var s = k.OpenSubKey(id + @"\Settings\1186"))
                            if (s != null) foreach (var sub in s.GetSubKeyNames()) yield return $@"{Ids}\{id}\Settings\1186\{sub}";
            }

            public override Dictionary<string, string> Read()
            {
                var d = new Dictionary<string, string>();
                using (var k = Registry.CurrentUser.OpenSubKey(Common)) d["UI Theme"] = k?.GetValue("UI Theme") is int n ? n.ToString() : null;
                foreach (var r in RoamingKeys())
                    using (var k = Registry.CurrentUser.OpenSubKey(r))
                        if (k?.GetValue("Data") is byte[] b && b.Length == 4) d[r] = BitConverter.ToInt32(b, 0).ToString();
                return d;
            }

            public override Dictionary<string, string> Changes(Dictionary<string, string> now) =>
                now.Where(kv => kv.Value != "6" && (kv.Key == "UI Theme" || kv.Value != null)).ToDictionary(kv => kv.Key, kv => "6");

            public override void Write(Dictionary<string, string> values)
            {
                foreach (var kv in values)
                {
                    if (kv.Key == "UI Theme")
                    {
                        Backup.Save(@"HKCU\" + Common);
                        using (var k = Registry.CurrentUser.CreateSubKey(Common))
                            if (kv.Value == null) k.DeleteValue("UI Theme", false); else k.SetValue("UI Theme", int.Parse(kv.Value), RegistryValueKind.DWord);
                    }
                    else if (kv.Value != null)
                        using (var k = Registry.CurrentUser.OpenSubKey(kv.Key, true))
                            k?.SetValue("Data", BitConverter.GetBytes(int.Parse(kv.Value)), RegistryValueKind.Binary);
                    Log.Info($"Office {(kv.Key == "UI Theme" ? "UI Theme" : "account copy")}: {kv.Value ?? "not set"}");
                }
            }
        }

        // One setting in a JSON (or JSON-with-comments) settings file the app reloads by itself.
        public class JsonSetting : FollowApp
        {
            readonly Func<string> file; readonly string key, value, process, store; readonly string[] exes; readonly bool absentFollows, waits;
            public JsonSetting(string id, string name, string followName, string process, bool waitsForClose, string key, string value, bool absentFollows,
                Func<string> file, string store = null, string[] exes = null)
            {
                Id = id; Name = name; FollowName = followName; this.process = process; waits = waitsForClose; this.key = key; this.value = value;
                this.absentFollows = absentFollows; this.file = file; this.store = store; this.exes = exes ?? new string[0];
            }
            public override bool Automatic => true;
            public override bool WaitsForClose => waits;
            public override string[] Processes => new[] { process };
            public override bool Installed => exes.Any(File.Exists) || (store != null && FindPackage(store) != null);
            public override string Exe => exes.FirstOrDefault(File.Exists);
            public override string Logo => store != null ? FindPackage(store)?.Logo : null;
            public override void Open()
            {
                if (Exe != null) { Shell.AsUser(Exe, "", null); return; }
                var p = store != null ? FindPackage(store) : null;
                if (p?.AppId != null) Shell.AsUser("explorer.exe", $"shell:AppsFolder\\{p.Family}!{p.AppId}", null);
            }

            public override Dictionary<string, string> Read() =>
                new Dictionary<string, string> { [key] = File.Exists(file()) ? JsonText.Get(File.ReadAllText(file(), Encoding.UTF8), key) : null };

            public override Dictionary<string, string> Changes(Dictionary<string, string> now)
            {
                now.TryGetValue(key, out var v);
                bool follows = v == null ? absentFollows : v == value;
                return follows ? new Dictionary<string, string>() : new Dictionary<string, string> { [key] = value };
            }

            public override void Write(Dictionary<string, string> values) => JsonText.WriteFile(file(), values, Id);
        }

        // VS Code and Cursor: "window.autoDetectColorScheme" switches between the preferred light and dark themes as
        // Windows switches. Someone's own dark (or light) theme becomes the preferred one, so it stays theirs.
        public class CodeEditor : FollowApp
        {
            readonly string settings, process; readonly string[] exes;
            const string Auto = "window.autoDetectColorScheme", Theme = "workbench.colorTheme",
                PrefDark = "workbench.preferredDarkColorTheme", PrefLight = "workbench.preferredLightColorTheme";
            public CodeEditor(string id, string name, string process, string settings, params string[] exes)
            { Id = id; Name = name; FollowName = "light and dark themes"; this.process = process; this.settings = settings; this.exes = exes; }
            public override bool Automatic => true;
            public override string[] Processes => new[] { process };
            public override bool Installed => exes.Any(File.Exists);
            public override string Exe => exes.FirstOrDefault(File.Exists);

            public override Dictionary<string, string> Read()
            {
                var text = File.Exists(settings) ? File.ReadAllText(settings, Encoding.UTF8) : "{}";
                return new[] { Auto, Theme, PrefDark, PrefLight }.ToDictionary(k => k, k => JsonText.Get(text, k));
            }

            public override Dictionary<string, string> Changes(Dictionary<string, string> now)
            {
                var ch = new Dictionary<string, string>();
                if (now[Auto] != "true") ch[Auto] = "true";
                var theme = now[Theme];
                if (theme != null)
                {
                    var pref = theme.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0 ? PrefLight : PrefDark;
                    if (now[pref] == null) ch[pref] = theme;
                }
                return ch;
            }

            public override void Write(Dictionary<string, string> values) => JsonText.WriteFile(settings, values.Where(kv => kv.Key != Theme).ToDictionary(kv => kv.Key, kv => kv.Value), Id);
        }

        static void BackupOnce(string path, string name)
        {
            try
            {
                var dir = Path.Combine(Log.Dir, "backup");
                Directory.CreateDirectory(dir);
                var f = Path.Combine(dir, name);
                if (File.Exists(path) && !File.Exists(f)) File.Copy(path, f);
            }
            catch (Exception e) { Log.Warn("backup " + path + ": " + e.Message); }
        }

        // Edits single top-level values in a JSON or JSON-with-comments file as text, so everything else (comments,
        // order, spacing, the rest of the values) stays exactly as it was. Values are raw JSON: true, 0, "system".
        public static class JsonText
        {
            const string Scalar = "(\"(?:[^\"\\\\]|\\\\.)*\"|true|false|null|-?\\d+(?:\\.\\d+)?)";

            static Match Find(string text, string key)
            {
                foreach (Match m in new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*" + Scalar).Matches(text))
                {
                    int ls = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                    if (!text.Substring(ls, m.Index - ls).Contains("//")) return m;
                }
                return null;
            }

            public static string Get(string text, string key) => Find(text, key)?.Groups[1].Value;

            public static string Set(string text, string key, string value)
            {
                var m = Find(text, key);
                if (m != null)
                {
                    if (value != null) return text.Substring(0, m.Groups[1].Index) + value + text.Substring(m.Groups[1].Index + m.Groups[1].Length);
                    return Remove(text, m);
                }
                if (value == null) return text;
                // add it as the first value of the outer object
                int open = -1;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '{') { int ls = text.LastIndexOf('\n', Math.Max(0, i - 1)) + 1; if (!text.Substring(ls, i - ls).Contains("//")) { open = i; break; } }
                }
                if (open < 0) return "{\n    \"" + key + "\": " + value + "\n}\n";
                bool empty = text.Substring(open + 1).TrimStart().StartsWith("}");
                string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                int lineEnd = text.IndexOf('\n', open);
                bool pretty = lineEnd >= 0 && text.Substring(open + 1, lineEnd - open - 1).Trim().Length == 0;
                if (!pretty) return text.Substring(0, open + 1) + "\"" + key + "\":" + value + (empty ? "" : ",") + text.Substring(open + 1);
                var next = text.Substring(lineEnd + 1);
                var indent = new string(next.TakeWhile(c => c == ' ' || c == '\t').ToArray());
                if (indent.Length == 0 || empty) indent = "    ";
                return text.Substring(0, lineEnd + 1) + indent + "\"" + key + "\": " + value + (empty ? "" : ",") + nl + text.Substring(lineEnd + 1);
            }

            static string Remove(string text, Match m)
            {
                int start = m.Index, end = m.Index + m.Length;
                int j = end; while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
                if (j < text.Length && text[j] == ',') end = j + 1;
                else
                {
                    int k = start - 1; while (k >= 0 && char.IsWhiteSpace(text[k])) k--;
                    if (k >= 0 && text[k] == ',') start = k;
                }
                // a value on a line of its own takes its line with it
                int ls = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
                int le = text.IndexOf('\n', end); if (le < 0) le = text.Length - 1;
                if (text.Substring(ls, start - ls).Trim().Length == 0 && text.Substring(end, le - end).Trim().Length == 0) { start = ls; end = le + 1; }
                return text.Substring(0, start) + text.Substring(Math.Min(end, text.Length));
            }

            public static void WriteFile(string path, Dictionary<string, string> values, string backupName)
            {
                bool exists = File.Exists(path);
                if (!exists && values.All(v => v.Value == null)) return;
                var bytes = exists ? File.ReadAllBytes(path) : new byte[0];
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var text = exists ? new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)) : "{\n}\n";
                var after = text;
                foreach (var kv in values) after = Set(after, kv.Key, kv.Value);
                if (after == text) return;
                if (exists) BackupOnce(path, backupName + "-" + Path.GetFileName(path) + ".orig");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, after, new UTF8Encoding(bom));
                foreach (var kv in values) Log.Info($"{backupName} {kv.Key}: {kv.Value ?? "removed"}");
            }
        }
    }
}
