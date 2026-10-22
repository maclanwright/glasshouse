using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Glasshouse
{
    // One Windhawk mod: which build, where it loads, and every setting exactly as the engine stores it
    // (flattened keys like "controlStyles[2].styles[0]"; whole numbers are DWORDs, the rest strings).
    public class ModSpec
    {
        public string Id, Version, Include = "", Exclude = "", Architecture = "";
        public bool Disabled;
        public Dictionary<string, object> Settings = new Dictionary<string, object>();

        public ModSpec Clone() => new ModSpec
        {
            Id = Id, Version = Version, Include = Include, Exclude = Exclude, Architecture = Architecture, Disabled = Disabled,
            Settings = new Dictionary<string, object>(Settings)
        };

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["id"] = Id, ["version"] = Version, ["include"] = Include, ["exclude"] = Exclude,
            ["architecture"] = Architecture, ["disabled"] = Disabled,
            ["settings"] = Settings.OrderBy(k => k.Key, SettingKeyOrder.Instance).ToDictionary(k => k.Key, k => k.Value)
        };

        public static ModSpec FromJson(Dictionary<string, object> d)
        {
            var m = new ModSpec
            {
                Id = d.Str("id"), Version = d.Str("version"), Include = d.Str("include", ""), Exclude = d.Str("exclude", ""),
                Architecture = d.Str("architecture", ""), Disabled = d.Bool("disabled")
            };
            var s = d.Obj("settings");
            if (s != null) foreach (var kv in s) m.Settings[kv.Key] = kv.Value is double dd ? (object)(long)dd : kv.Value;
            if (string.IsNullOrEmpty(m.Id) || !Regex.IsMatch(m.Id, "^[a-z0-9][a-z0-9-]*$")) throw new FormatException("bad mod id: " + m.Id);
            if (string.IsNullOrEmpty(m.Version) || !Regex.IsMatch(m.Version, @"^[0-9A-Za-z.\-]+$")) throw new FormatException("bad version for " + m.Id);
            return m;
        }
    }

    // A bit of the look that only makes sense on the machine it was saved on (e.g. hiding Parsec's helper
    // button, has no Start pins, can't pair a phone). Off by default when the look is opened anywhere else.
    public class PersonalItem
    {
        public string Id, Label, Detail;
        public PersonalItem(string id, string label, string detail = "") { Id = id; Label = label; Detail = detail; }

        // The known personal bits. "rule" items are a styler rule found by a word in its target.
        public static readonly PersonalItem[] Known =
        {
            new PersonalItem("parsec-helper", "Hide Parsec's helper button in the Dock", "Parsec makes a blank helper window on the computer it streams from"),
            new PersonalItem("start-pins-hidden", "Hide the pinned apps area in Start", "For a Start menu with nothing pinned"),
            new PersonalItem("phone-panel-hidden", "Hide the phone panel in Start", "Phone Link's panel beside Start"),
        };
        public static readonly Dictionary<string, (string mod, string match)> Rules = new Dictionary<string, (string, string)>
        {
            ["parsec-helper"] = ("windows-11-taskbar-styler", "ParsecMinFrameRate"),
            ["start-pins-hidden"] = ("windows-11-start-menu-styler", "StartMenu.PinnedList#StartMenuPinnedList"),
        };
    }

    public class LightDark
    {
        public bool Enabled = true;          // Auto Dark Mode looks after it
        public string Apps = "switch";       // switch | light | dark
        public string Windows = "dark";      // switch | light | dark  (taskbar, Start, File Explorer)
        public string Place = "";            // a name for people; the numbers below are what counts
        public double Lat, Lon;
        public bool UseWindowsLocation;      // sunrise/sunset where Windows says this computer is (for friends)
        public bool CustomTimes;             // true = the two times below instead of sunrise/sunset
        public string LightAt = "07:00", DarkAt = "20:00";

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["enabled"] = Enabled, ["apps"] = Apps, ["windows"] = Windows, ["place"] = Place, ["lat"] = Lat, ["lon"] = Lon,
            ["windowsLocation"] = UseWindowsLocation, ["customTimes"] = CustomTimes, ["lightAt"] = LightAt, ["darkAt"] = DarkAt
        };
        public static LightDark FromJson(Dictionary<string, object> d) => d == null ? null : new LightDark
        {
            Enabled = d.Bool("enabled", true), Apps = d.Str("apps", "switch"), Windows = d.Str("windows", "dark"), Place = d.Str("place", ""),
            Lat = d.Dbl("lat"), Lon = d.Dbl("lon"), UseWindowsLocation = d.Bool("windowsLocation"), CustomTimes = d.Bool("customTimes"), LightAt = d.Str("lightAt", "07:00"), DarkAt = d.Str("darkAt", "20:00")
        };
    }

    // Plain Windows settings that are part of the look.
    public class Tidy
    {
        public bool AutoHideDock = true;      // Windows' taskbar auto-hide (the Dock mod then slides in from the bottom)
        public bool Transparency = true;      // Windows' "Transparency effects" (the glass needs it)
        public bool HideDesktopIcons = true;
        public bool HideTaskbarExtras = true; // Task view, Copilot, Chat, search box buttons
        public bool StartNoRecent = true;     // no recent files, recommendations or account nags in Start
        public bool AccentTitleBars = true;   // accent colour on title bars and window borders
        public bool QuietSounds = true;       // Windows' system sounds off
        public bool DarkTaskManager = true;   // Task Manager's own dark theme (in light mode the glass leaves its list black with black names)

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["autoHideDock"] = AutoHideDock, ["transparency"] = Transparency, ["hideDesktopIcons"] = HideDesktopIcons,
            ["hideTaskbarExtras"] = HideTaskbarExtras, ["startNoRecent"] = StartNoRecent, ["accentTitleBars"] = AccentTitleBars,
            ["quietSounds"] = QuietSounds, ["darkTaskManager"] = DarkTaskManager
        };
        public static Tidy FromJson(Dictionary<string, object> d) => d == null ? new Tidy() : new Tidy
        {
            AutoHideDock = d.Bool("autoHideDock", true), Transparency = d.Bool("transparency", true), HideDesktopIcons = d.Bool("hideDesktopIcons", true),
            HideTaskbarExtras = d.Bool("hideTaskbarExtras", true), StartNoRecent = d.Bool("startNoRecent", true),
            AccentTitleBars = d.Bool("accentTitleBars", true), QuietSounds = d.Bool("quietSounds", true), DarkTaskManager = d.Bool("darkTaskManager", true)
        };
    }

    public class Look
    {
        public const string Extension = ".glasshouse";
        public string Name = "My look", SavedFrom = "", SavedAt = "", Accent = "#F05A22";
        public List<ModSpec> Mods = new List<ModSpec>();
        public LightDark LightDark = new LightDark();
        public string Wallpaper = "Catalina";      // a WinDynamicDesktop theme id, "" = leave the wallpaper alone
        public bool MacPointers = true;
        public Dictionary<string, bool> Apps = new Dictionary<string, bool>
        {
            ["raycast"] = true, ["powertoys"] = true, ["fileConverter"] = true
        };
        public Tidy Tidy = new Tidy();
        public HashSet<string> Personal = new HashSet<string>();   // personal bits that are ON in this look

        public ModSpec Mod(string id) => Mods.FirstOrDefault(m => m.Id == id);

        public string ToJsonText()
        {
            var d = new Dictionary<string, object>
            {
                ["glasshouse"] = 1,
                ["name"] = Name, ["savedFrom"] = SavedFrom, ["savedAt"] = SavedAt,
                ["accent"] = Accent,
                ["lightDark"] = LightDark?.ToJson(),
                ["wallpaper"] = Wallpaper,
                ["macPointers"] = MacPointers,
                ["apps"] = Apps.ToDictionary(k => k.Key, k => (object)k.Value),
                ["tidy"] = Tidy.ToJson(),
                ["personal"] = PersonalItem.Known.Where(p => Personal.Contains(p.Id)).Select(p => (object)new Dictionary<string, object>
                    { ["id"] = p.Id, ["label"] = p.Label, ["on"] = Personal.Contains(p.Id) }).ToList(),
                ["mods"] = Mods.Select(m => (object)m.ToJson()).ToList()
            };
            return Json.Write(d) + "\n";
        }

        public static Look FromJsonText(string text)
        {
            var d = Json.ParseObject(text);
            if (d.Long("glasshouse") < 1) throw new FormatException("not a Glasshouse file");
            var l = new Look
            {
                Name = d.Str("name", "Look"), SavedFrom = d.Str("savedFrom", ""), SavedAt = d.Str("savedAt", ""),
                Accent = Colors.Normalise(d.Str("accent", "#F05A22")),
                LightDark = LightDark.FromJson(d.Obj("lightDark")),
                Wallpaper = d.Str("wallpaper", ""), MacPointers = d.Bool("macPointers", true), Tidy = Tidy.FromJson(d.Obj("tidy"))
            };
            var apps = d.Obj("apps");
            if (apps != null) foreach (var k in apps.Keys.ToList()) l.Apps[k] = apps.Bool(k);
            foreach (var p in d.Arr("personal") ?? new List<object>())
                if (p is Dictionary<string, object> pd && pd.Bool("on")) l.Personal.Add(pd.Str("id"));
            foreach (var m in d.Arr("mods") ?? new List<object>())
                l.Mods.Add(ModSpec.FromJson((Dictionary<string, object>)m));
            return l;
        }

        public static Look Load(string path) => FromJsonText(File.ReadAllText(path, Encoding.UTF8));
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, ToJsonText(), new UTF8Encoding(false));
        }

        // The look as it should land on this machine: personal bits filtered, safety exclusions added, themes named
        // by their page read from it (a mod whose theme can't be read is left out, so the machine keeps what it has).
        public List<ModSpec> ModsFor(ISet<string> personalOn, bool readThemes = true)
        {
            var list = new List<ModSpec>();
            foreach (var m0 in Mods)
            {
                var m = m0.Clone();
                foreach (var r in PersonalItem.Rules)
                    if (r.Value.mod == m.Id && !personalOn.Contains(r.Key))
                        Settings.RemoveArrayItems(m.Settings, "controlStyles", t => t.Contains(r.Value.match));
                if (m.Id == Windhawk.GlassModId) m.Exclude = Windhawk.WithSafetyExcludes(m.Exclude);
                if (readThemes && ThemeImport.Has(m))
                    try { ThemeImport.Expand(m); }
                    catch (Exception e) { Log.Warn($"{m.Id} left as it is: {e.Message}"); continue; }
                list.Add(m);
            }
            return list;
        }
    }

    // Flattened-array helpers. Windhawk mods read an array until the first missing index, so items must stay
    // numbered 0, 1, 2... with no gaps: removing one renumbers everything after it.
    public static class Settings
    {
        static readonly Regex Item = new Regex(@"^(?<arr>[A-Za-z0-9_.]+)\[(?<i>\d+)\](?<rest>.*)$");

        public static int Count(Dictionary<string, object> s, string arr)
        {
            int max = -1;
            foreach (var k in s.Keys)
            {
                var m = Item.Match(k);
                if (m.Success && m.Groups["arr"].Value == arr) max = Math.Max(max, int.Parse(m.Groups["i"].Value));
            }
            return max + 1;
        }

        // Remove array items whose ".target" (or another field, such as ".path") matches; returns how many went.
        public static int RemoveArrayItems(Dictionary<string, object> s, string arr, Func<string, bool> targetMatches, string field = "target")
        {
            int n = Count(s, arr);
            var drop = new HashSet<int>();
            for (int i = 0; i < n; i++)
                if (s.TryGetValue($"{arr}[{i}].{field}", out var t) && t is string ts && targetMatches(ts)) drop.Add(i);
            if (drop.Count == 0) return 0;
            var moved = new Dictionary<string, object>();
            foreach (var k in s.Keys.ToList())
            {
                var m = Item.Match(k);
                if (!m.Success || m.Groups["arr"].Value != arr) continue;
                int i = int.Parse(m.Groups["i"].Value);
                var v = s[k];
                s.Remove(k);
                if (drop.Contains(i)) continue;
                int ni = i - drop.Count(x => x < i);
                moved[$"{arr}[{ni}]{m.Groups["rest"].Value}"] = v;
            }
            foreach (var kv in moved) s[kv.Key] = kv.Value;
            return drop.Count;
        }

        // Index of the first item whose target matches, or -1.
        public static int FindItem(Dictionary<string, object> s, string arr, Func<string, bool> targetMatches)
        {
            int n = Count(s, arr);
            for (int i = 0; i < n; i++)
                if (s.TryGetValue($"{arr}[{i}].target", out var t) && t is string ts && targetMatches(ts)) return i;
            return -1;
        }

        // Replace (or add at the end) one rule: target + its style lines.
        public static void SetRule(Dictionary<string, object> s, string arr, int index, string target, IList<string> styles)
        {
            foreach (var k in s.Keys.Where(k => k.StartsWith($"{arr}[{index}].")).ToList()) s.Remove(k);
            s[$"{arr}[{index}].target"] = target;
            for (int j = 0; j < styles.Count; j++) s[$"{arr}[{index}].styles[{j}]"] = styles[j];
        }

        public static List<string> RuleStyles(Dictionary<string, object> s, string arr, int index)
        {
            var l = new List<string>();
            for (int j = 0; s.TryGetValue($"{arr}[{index}].styles[{j}]", out var v); j++) l.Add(Convert.ToString(v));
            return l;
        }
    }

    // Sorts "a[2].b" before "a[10].b" so saved files read in order.
    class SettingKeyOrder : IComparer<string>
    {
        public static readonly SettingKeyOrder Instance = new SettingKeyOrder();
        static readonly Regex Parts = new Regex(@"\d+|\D+");
        public int Compare(string a, string b)
        {
            var pa = Parts.Matches(a); var pb = Parts.Matches(b);
            for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++)
            {
                string x = pa[i].Value, y = pb[i].Value;
                int c = char.IsDigit(x[0]) && char.IsDigit(y[0]) ? long.Parse(x).CompareTo(long.Parse(y)) : string.CompareOrdinal(x, y);
                if (c != 0) return c;
            }
            return pa.Count.CompareTo(pb.Count);
        }
    }
}
