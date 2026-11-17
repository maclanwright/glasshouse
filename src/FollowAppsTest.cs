using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Glasshouse
{
    // --follow-selftest <folder>: the settings-file edits behind "Apps that switch with it", done on made-up files in
    // a scratch folder (never the real apps): set to follow Windows, then put back, must give back the same bytes.
    public static class FollowAppsTest
    {
        static int fails;
        static void Check(bool ok, string what) { if (!ok) fails++; Log.Info((ok ? "ok    " : "FAIL  ") + what); }

        public static int Run(string dir)
        {
            fails = 0;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var J = (Func<string, string, string, string>)FollowApps.JsonText.Set;

            // ---- JSON text edits ----
            var pretty = "// my settings\r\n{\r\n    \"a\": 1,\r\n    // \"theme\": \"dark\",\r\n    \"themes\": [],\r\n    \"b\": \"x\"\r\n}\r\n";
            var added = J(pretty, "theme", "\"system\"");
            Check(added == "// my settings\r\n{\r\n    \"theme\": \"system\",\r\n    \"a\": 1,\r\n    // \"theme\": \"dark\",\r\n    \"themes\": [],\r\n    \"b\": \"x\"\r\n}\r\n", "adds a value as the first line, skips the commented-out one and \"themes\"");
            Check(J(added, "theme", null) == pretty, "removing it gives back the same text");
            Check(J(pretty, "b", "\"y\"").Contains("\"b\": \"y\"") && J(pretty, "b", "\"y\"").Length == pretty.Length, "changes a value in place");
            var last = J(pretty, "b", null);
            Check(last == "// my settings\r\n{\r\n    \"a\": 1,\r\n    // \"theme\": \"dark\",\r\n    \"themes\": []\r\n}\r\n", "removing the last value takes the comma before it");
            Check(J("{}", "k", "true") == "{\"k\":true}", "empty compact object");
            Check(J("{\n}\n", "k", "true") == "{\n    \"k\": true\n}\n", "empty pretty object");
            Check(J("{\"x\":1,\"theme\":\"dark\",\"y\":2}", "theme", "\"system\"") == "{\"x\":1,\"theme\":\"system\",\"y\":2}", "compact file (PowerToys)");
            Check(J("{\"x\":1,\"theme\":\"dark\",\"y\":2}", "theme", null) == "{\"x\":1,\"y\":2}", "compact remove");
            Check(FollowApps.JsonText.Get("{\"s\":\"a \\\"q\\\" b\"}", "s") == "\"a \\\"q\\\" b\"", "strings with escaped quotes");

            // ---- a Chromium browser with three profiles ----
            var ud = Path.Combine(dir, "User Data");
            var prefs = new Dictionary<string, string>
            {
                ["Default"] = "{\"browser\":{\"theme\":{\"color_scheme\":2,\"color_scheme2\":2}},\"x\":\"é\"}",
                ["Profile 1"] = "{\"browser\":{\"window\":1}}",
                ["Profile 2"] = "{\"browser\":{\"theme\":{\"color_scheme2\":0}}}",
                ["System Profile"] = "{\"browser\":{\"theme\":{\"color_scheme2\":1}}}",
            };
            foreach (var p in prefs) { Directory.CreateDirectory(Path.Combine(ud, p.Key)); File.WriteAllText(Path.Combine(ud, p.Key, "Preferences"), p.Value, new UTF8Encoding(false)); }
            var exe = Path.Combine(dir, "browser.exe"); File.WriteAllText(exe, "");
            var b = new FollowApps.Chromium("testbrowser", "Test browser", "Device", "no-such-process-glasshouse", ud, exe);
            var now = b.Read();
            Check(now.Count == 3 && now["Default"] == "2" && now["Profile 1"] == null && now["Profile 2"] == "0", "reads each profile (not the System Profile)");
            var ch = b.Changes(now);
            Check(ch.Count == 1 && ch["Default"] == "0", "only the Dark profile needs changing");
            b.Write(ch);
            Check(File.ReadAllText(Path.Combine(ud, "Default", "Preferences"), Encoding.UTF8) == prefs["Default"].Replace("\"color_scheme2\":2", "\"color_scheme2\":0"), "sets it to follow; the old \"color_scheme\" and other text untouched");
            Check(b.Follows, "now follows");
            b.Write(new Dictionary<string, string> { ["Default"] = "2" });
            Check(File.ReadAllText(Path.Combine(ud, "Default", "Preferences"), Encoding.UTF8) == prefs["Default"], "putting it back gives the same file");
            Check(File.ReadAllText(Path.Combine(ud, "System Profile", "Preferences"), Encoding.UTF8) == prefs["System Profile"], "System Profile never touched");

            // ---- VS Code style settings ----
            var vs = Path.Combine(dir, "Code", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(vs));
            var vsText = "{\n    \"editor.fontSize\": 14,\n    \"workbench.colorTheme\": \"One Dark Pro\"\n}\n";
            File.WriteAllText(vs, vsText, new UTF8Encoding(false));
            var code = new FollowApps.CodeEditor("testcode", "Test editor", "no-such-process-glasshouse", vs, exe);
            var cn = code.Read(); var cc = code.Changes(cn);
            Check(cc.Count == 2 && cc["window.autoDetectColorScheme"] == "true" && cc["workbench.preferredDarkColorTheme"] == "\"One Dark Pro\"", "keeps your own dark theme as the dark one");
            var before = cc.Keys.ToDictionary(k => k, k => cn[k]);
            code.Write(cc);
            Check(code.Follows, "editor follows");
            code.Write(before);
            Check(File.ReadAllText(vs) == vsText, "putting it back gives the same file");
            File.Delete(vs);
            code.Write(new Dictionary<string, string> { ["window.autoDetectColorScheme"] = "true" });
            Check(File.ReadAllText(vs) == "{\n    \"window.autoDetectColorScheme\": true\n}\n", "no settings file yet: makes one");

            Log.Info(fails == 0 ? "follow self-test: all passed" : $"follow self-test: {fails} FAILED");
            return fails == 0 ? 0 : 1;
        }
    }
}
