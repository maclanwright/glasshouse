using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Glasshouse
{
    // A look can name a styler theme by its page instead of carrying a copy of it: the setting "@import" holds the
    // address of the theme's README in a styling guide (ramensoftware/windows-11-*-styling-guide). Its first ```yaml
    // block is read and becomes the mod's settings; the look's own array items (extra rules) go after the theme's.
    // Like every other part of Glasshouse it's downloaded from its author's page, never shipped. The last copy is
    // kept in %LOCALAPPDATA%\Glasshouse\themes so it still works offline.
    public static class ThemeImport
    {
        public const string Key = "@import";
        static readonly string[] Arrays = { "styleConstants", "themeResourceVariables", "controlStyles" };

        public static bool Has(ModSpec m) => m.Settings.ContainsKey(Key);

        // Replaces "@import" with the theme's settings. Throws when it can't be downloaded and isn't cached.
        public static void Expand(ModSpec m)
        {
            if (!(m.Settings.TryGetValue(Key, out var u) && u is string url)) return;
            var theme = Parse(Fetch(url));
            var own = m.Settings.Where(kv => kv.Key != Key).ToDictionary(kv => kv.Key, kv => kv.Value);
            var merged = new Dictionary<string, object>(theme);
            foreach (var kv in own)
            {
                var arr = Arrays.FirstOrDefault(a => kv.Key.StartsWith(a + "["));
                if (arr == null) { merged[kv.Key] = kv.Value; continue; }
                int open = kv.Key.IndexOf('['), close = kv.Key.IndexOf(']');
                int i = int.Parse(kv.Key.Substring(open + 1, close - open - 1));
                // an empty first item is the styler's "nothing here" placeholder: not worth adding
                if (Settings.Count(own, arr) == 1 && IsEmptyItem(own, arr, 0)) continue;
                merged[$"{arr}[{i + Settings.Count(theme, arr)}]{kv.Key.Substring(close + 1)}"] = kv.Value;
            }
            m.Settings.Clear();
            foreach (var kv in merged) m.Settings[kv.Key] = kv.Value;
            Log.Info($"  {m.Id}: theme read from {url} ({theme.Count} settings)");
        }

        static bool IsEmptyItem(Dictionary<string, object> s, string arr, int i) =>
            s.Where(kv => kv.Key.StartsWith($"{arr}[{i}]")).All(kv => (kv.Value as string ?? "") == "");

        static string Fetch(string url)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glasshouse", "themes");
            string name;
            using (var sha = SHA1.Create()) name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "").Substring(0, 16) + ".yaml";
            var cache = Path.Combine(dir, name);
            try
            {
                var page = Encoding.UTF8.GetString(Net.Get(url));
                int start = page.IndexOf("```yaml", StringComparison.Ordinal);
                if (start < 0) throw new FormatException("no ```yaml block on " + url);
                start = page.IndexOf('\n', start) + 1;
                int end = page.IndexOf("```", start, StringComparison.Ordinal);
                var yaml = page.Substring(start, (end < 0 ? page.Length : end) - start);
                Directory.CreateDirectory(dir);
                File.WriteAllText(cache, yaml, new UTF8Encoding(false));
                return yaml;
            }
            catch (Exception e)
            {
                if (File.Exists(cache)) { Log.Warn($"couldn't download the theme ({e.Message}): using the copy from last time"); return File.ReadAllText(cache); }
                throw new InvalidOperationException($"couldn't download the theme from {url}: {e.Message}");
            }
        }

        // The small part of YAML the styling guides use: top-level lists of strings, and controlStyles as a list of
        // { target, styles: [..] }.
        public static Dictionary<string, object> Parse(string yaml)
        {
            var d = new Dictionary<string, object>();
            string section = null; int item = -1, style = 0, n = 0;
            foreach (var raw in yaml.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#")) continue;
                if (!char.IsWhiteSpace(raw[0]))
                {
                    section = raw.Trim().TrimEnd(':'); item = -1; n = 0;
                    continue;
                }
                var t = raw.Trim();
                if (section == "controlStyles")
                {
                    if (t.StartsWith("- target:")) { item++; style = 0; d[$"controlStyles[{item}].target"] = Unquote(t.Substring(9).Trim()); }
                    else if (t.StartsWith("target:")) d[$"controlStyles[{item}].target"] = Unquote(t.Substring(7).Trim());
                    else if (t == "styles:") continue;
                    else if (t.StartsWith("- ") && item >= 0) d[$"controlStyles[{item}].styles[{style++}]"] = Unquote(t.Substring(2).Trim());
                }
                else if (section != null && Arrays.Contains(section) && t.StartsWith("- "))
                    d[$"{section}[{n++}]"] = Unquote(t.Substring(2).Trim());
            }
            return d;
        }

        static string Unquote(string v)
        {
            if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') return v.Substring(1, v.Length - 2).Replace("''", "'");
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') return v.Substring(1, v.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            return v;
        }
    }
}
