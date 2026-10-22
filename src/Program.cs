using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Glasshouse
{
    public static class Program
    {
        public static string SelfPath => Assembly.GetExecutingAssembly().Location;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        public static int Main(string[] args)
        {
            var a = new Args(args);
            string report = a.Value("--report");
            bool console = report == null && args.Length > 0 && AttachConsole(-1);
            if (console) Log.Line += l => { try { Console.WriteLine(l); } catch { } };
            int code = 0;
            try
            {
                if (a.Has("--helper")) { Elevation.HelperMain(); return 0; }
                if (a.Value("--elevated-job") is string jf) { Elevation.ElevatedJobMain(jf); return 0; }
                if (a.Has("--uninstall")) { Elevation.Uninstall(); return 0; }
                if (a.Has("--install-helper"))
                {
                    var r = Elevation.Run(new Job { Kind = "setup.admin", InstallSelf = true });
                    if (!r.Ok) throw new InvalidOperationException(r.Error);
                    Setup.RegisterFileType();
                    Log.Info("helper ready: " + Elevation.HelperReady);
                }
                else if (a.Value("--add-mod") is string newId)
                {
                    var rest = args.SkipWhile(x => x != "--add-mod").Skip(1).ToList();
                    var spec = new ModSpec { Id = newId, Version = rest[1], Include = a.Value("--include") ?? throw new ArgumentException("--include is needed (never *)") };
                    foreach (var kv in rest.Skip(2).Where(x => x.Contains("=") && !x.StartsWith("--")))
                    {
                        int eq = kv.IndexOf('='); var v = kv.Substring(eq + 1);
                        spec.Settings[kv.Substring(0, eq)] = long.TryParse(v, out var n) ? (object)n : v;
                    }
                    var r = Elevation.Run(new Job { Kind = "windhawk.apply", Mods = { spec } });
                    if (!r.Ok) throw new InvalidOperationException(r.Error);
                    Log.Info("added " + newId + " " + spec.Version);
                }
                else if (a.Value("--set") is string modId)
                {
                    // --set <mod> key=value [key=value...]: change single settings of an installed mod (numbers become DWORDs)
                    // array-=target removes the items of that array whose target is target (the rest are renumbered);
                    // exclude+=prog / exclude-=prog (and include+= / include-=) add a program to where the mod loads or take it off
                    var have = Windhawk.Read(modId) ?? throw new InvalidOperationException(modId + " isn't installed");
                    var spec = have.Spec;
                    // @file reads more key=value lines from a file; !clear empties the settings first (for swapping whole themes)
                    var pairs = args.SkipWhile(x => x != "--set").Skip(2).TakeWhile(x => !x.StartsWith("--"))
                        .SelectMany(x => x.StartsWith("@") ? File.ReadAllLines(x.Substring(1), Encoding.UTF8).Where(l => l.Length > 0) : new[] { x });
                    foreach (var kv in pairs)
                    {
                        if (kv == "!clear") { spec.Settings.Clear(); continue; }
                        if (kv == "!off" || kv == "!on") { spec.Disabled = kv == "!off"; continue; }
                        int eq = kv.IndexOf('=');
                        var k = kv.Substring(0, eq); var v = kv.Substring(eq + 1);
                        if (k == "exclude+" || k == "exclude-" || k == "include+" || k == "include-")
                        {
                            var list = k.StartsWith("exclude") ? spec.Exclude : spec.Include;
                            var parts = (list ?? "").Split('|').Where(p => p.Trim().Length > 0 && !p.Trim().Equals(v, StringComparison.OrdinalIgnoreCase)).ToList();
                            if (k.EndsWith("+")) parts.Add(v);
                            if (k.StartsWith("exclude")) spec.Exclude = string.Join("|", parts); else spec.Include = string.Join("|", parts);
                            continue;
                        }
                        if (k.EndsWith("-"))
                        {
                            var arr = k.TrimEnd('-');
                            if (Settings.RemoveArrayItems(spec.Settings, arr, t => t.Equals(v, StringComparison.OrdinalIgnoreCase)) == 0)
                                Settings.RemoveArrayItems(spec.Settings, arr, t => t.Equals(v, StringComparison.OrdinalIgnoreCase), "path");
                            continue;
                        }
                        spec.Settings[k] = long.TryParse(v, out var n) && !(spec.Settings.TryGetValue(k, out var old) && old is string) ? (object)n : v;
                    }
                    var r = Elevation.Run(new Job { Kind = "windhawk.apply", Mods = { spec } });
                    if (!r.Ok) throw new InvalidOperationException(r.Error);
                    if (r.ServicesRestarted) Tailscale.MakeSureItsUp();
                    Log.Info(r.Changed.Count > 0 ? "changed " + modId : "already like that");
                }
                else if (a.Value("--export") is string ef)
                {
                    var look = Machine.Capture(a.Value("--name"));
                    look.Save(ef);
                    Log.Info($"saved this machine's look to {ef} ({look.Mods.Count} mods, accent {look.Accent}, personal: {string.Join(", ", look.Personal)})");
                }
                else if (a.Value("--make-shareable") is string mf)
                {
                    var look = Look.Load(mf);
                    var share = Look.FromJsonText(look.ToJsonText());
                    share.Mods = look.ModsFor(new HashSet<string>(), readThemes: false);     // personal rules out
                    share.Personal.Clear();
                    share.Name = "Glasshouse"; share.SavedFrom = ""; share.SavedAt = "";
                    share.Accent = "#2283F0";
                    share.LightDark = new LightDark { Enabled = true, Apps = "switch", Windows = "dark", UseWindowsLocation = true, LightAt = "07:00", DarkAt = "19:00" };
                    var outPath = args.SkipWhile(x => x != "--make-shareable").Skip(2).FirstOrDefault() ?? throw new ArgumentException("--make-shareable <in> <out>");
                    share.Save(outPath);
                    Log.Info($"shareable look saved to {outPath} ({share.Mods.Count} mods, no personal bits, light/dark where the computer is)");
                }
                else if (a.Value("--plan") is string pf)
                {
                    var look = Look.Load(pf);
                    var diffs = Machine.Plan(look, Options(a, look));
                    foreach (var d in diffs) Log.Info(d.ToString());
                    Log.Info(diffs.All(d => !d.Any) ? "plan: every mod already matches" : $"plan: {diffs.Count(d => d.Any)} mod(s) would change");
                }
                else if (a.Value("--apply") is string af)
                {
                    var look = Look.Load(af);
                    Machine.Apply(look, Options(a, look));
                }
                else if (a.Has("--setup"))
                {
                    var look = a.Value("--look") is string lf ? Look.Load(lf) : BuiltInLook();
                    Setup.Run(look, Options(a, look), new HashSet<string>((a.Value("--skip") ?? "").Split(',').Where(s => s.Length > 0)));
                }
                else if (a.Has("--repair"))
                {
                    var look = Repair.WantedLook(a.Value("--look"));
                    Repair.Run(look, Options(a, look));
                }
                else if (a.Has("--status"))
                {
                    var look = Repair.WantedLook(a.Value("--look"));
                    foreach (var c in Machine.Status(look)) Log.Info(c.ToString());
                }
                else if (a.Value("--selftest") is string dir) SelfTest.Downloads(dir);
                else if (a.Has("--knobtest")) SelfTest.Knobs();
                else if (a.Value("--uitest") is string udir) code = Ui.UiTest.Run(udir);
                else if (a.Value("--render") is string rdir) code = Ui.MainWindow.RenderAll(rdir, a.Has("--dark") ? true : a.Has("--light") ? (bool?)false : null);
                else if (a.Has("--inspector-server"))
                {
                    Inspector.Server.RunForever();
                }
                else
                {
                    // the window (a .glasshouse file passed in opens straight into "open a look")
                    code = Ui.App.Run(args.FirstOrDefault(x => x.EndsWith(Look.Extension, StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                File.AppendAllText(Path.Combine(Log.Dir, "glasshouse.log"), e + "\r\n");
                code = 1;
            }
            if (report != null) File.WriteAllText(report, string.Join("\r\n", Log.Session) + "\r\n", new UTF8Encoding(false));
            return code;
        }

        static Machine.Options Options(Args a, Look look)
        {
            var o = new Machine.Options { DryRun = a.Has("--dry-run"), NoRestart = a.Has("--no-restart"), Accent = a.Value("--accent") };
            var p = a.Value("--personal");
            if (p == "none") o.PersonalOn = new HashSet<string>();
            else if (p == "all") o.PersonalOn = new HashSet<string>(look.Personal);
            else if (p != null) o.PersonalOn = new HashSet<string>(p.Split(',').Select(s => s.Trim()));
            var only = a.Value("--only");
            if (only != null)
            {
                var s = new HashSet<string>(only.Split(','));
                o.Mods = s.Contains("mods"); o.LightDark = s.Contains("lightdark"); o.Wallpaper = s.Contains("wallpaper");
                o.Pointers = s.Contains("pointers"); o.Tidy = s.Contains("tidy"); o.Apps = s.Contains("apps");
            }
            return o;
        }

        // The look Glasshouse ships with (looks\Glasshouse.glasshouse, embedded).
        public static Look BuiltInLook()
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Look.Extension));
            if (name == null) throw new InvalidOperationException("no built-in look");
            using (var s = asm.GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8)) return Look.FromJsonText(r.ReadToEnd());
        }

        public static string Resource(string logicalName)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }
    }

    class Args
    {
        readonly string[] a;
        public Args(string[] args) { a = args; }
        public bool Has(string k) => a.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
        public string Value(string k)
        {
            int i = Array.FindIndex(a, x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }

    public static class Repair
    {
        // What "right" means here: the look this machine was last set to.
        public static Look WantedLook(string path = null)
        {
            if (path != null) return Look.Load(path);
            if (File.Exists(Machine.ThisMachineLook)) return Look.Load(Machine.ThisMachineLook);
            // first time on a machine that was styled before Glasshouse existed (set up by hand): today's state is the look
            if (!Windhawk.IsInstalled) return null;
            var now = Machine.Capture();
            now.Save(Machine.ThisMachineLook);
            return now;
        }

        public static void Run(Look look, Machine.Options o)
        {
            if (look == null) throw new InvalidOperationException("nothing to repair to yet: set up or open a look first");
            Log.Info("Repair: putting back \"" + look.Name + "\"");
            if (Sac.State == Sac.Mode.On) Log.Warn("Smart App Control " + Sac.Describe());
            if (!Windhawk.ServiceRunning && Windhawk.IsInstalled)
            {
                Log.Warn("Windhawk isn't running: starting it");
                Shell.Run("sc.exe", "start Windhawk", 30);
            }
            // a repair restarts the taskbar once so everything (hover styles above all) loads fresh
            var r = Machine.Apply(look, o);
            if (!r.RestartedTaskbar && !o.NoRestart && !o.DryRun) Shell.RestartExplorer();
            if (look.Tidy.AutoHideDock && !o.DryRun) Shell.AutoHide = true;
        }
    }
}
