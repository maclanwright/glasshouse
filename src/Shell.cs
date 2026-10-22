using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace Glasshouse
{
    // Windows' own settings that are part of the look, plus the plumbing to apply them without restarts or
    // pop-up windows. Everything here is per user and runs in the user's desktop.
    public static class Shell
    {
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr w, string l, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);

        // False in a background session (SSH, a service): no taskbar there, so desktop changes are skipped.
        public static bool HasDesktop => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

        public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public static void Broadcast(string what)
        {
            SendMessageTimeout((IntPtr)0xffff, 0x1A, UIntPtr.Zero, what, 2, 3000, out _);   // WM_SETTINGCHANGE, SMTO_ABORTIFHUNG
        }

        // Seconds since the last key press or mouse move: don't take over the screen while someone is typing.
        public static double IdleSeconds
        {
            get { var i = new LASTINPUTINFO { cbSize = 8 }; return GetLastInputInfo(ref i) ? (Environment.TickCount - i.dwTime) / 1000.0 : 999; }
        }

        // ---------- the Dock's auto-hide (Windows' taskbar auto-hide) ----------

        public static bool AutoHide
        {
            get
            {
                var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)) };
                return ((ulong)SHAppBarMessage(4, ref d) & 1) != 0;   // ABM_GETSTATE, ABS_AUTOHIDE
            }
            set
            {
                if (!HasDesktop) { Log.Warn("no desktop here: Dock auto-hide left as it is"); return; }
                if (AutoHide == value) return;
                var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = FindWindow("Shell_TrayWnd", null), lParam = (IntPtr)(value ? 1 : 0) };
                SHAppBarMessage(10, ref d);   // ABM_SETSTATE: takes effect at once and Windows saves it
                Log.Info("Dock auto-hide " + (value ? "on" : "off"));
            }
        }

        // the saved setting, for when there's no taskbar to ask
        static bool StuckRectsAutoHide
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3"))
                    return k?.GetValue("Settings") is byte[] b && b.Length > 8 && (b[8] & 1) != 0;
            }
        }

        // ---------- small per-user registry settings ----------

        static int GetDword(string key, string name, int def = -1)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(key)) return k?.GetValue(name) is int v ? v : def;
        }

        // Sets a DWORD if it differs; returns true when it changed.
        static bool SetDword(string key, string name, int value)
        {
            if (GetDword(key, name) == value) return false;
            using (var k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue(name, value, RegistryValueKind.DWord);
            return true;
        }

        public static Tidy ReadTidy() => new Tidy
        {
            AutoHideDock = HasDesktop ? AutoHide : StuckRectsAutoHide,
            Transparency = GetDword(Personalize, "EnableTransparency", 1) == 1,
            HideDesktopIcons = GetDword(Adv, "HideIcons", 0) == 1,
            HideTaskbarExtras = GetDword(Adv, "ShowTaskViewButton", 1) == 0 && GetDword(Adv, "ShowCopilotButton", 0) == 0,
            StartNoRecent = GetDword(Adv, "Start_TrackDocs", 1) == 0,
            AccentTitleBars = GetDword(@"Software\Microsoft\Windows\DWM", "ColorPrevalence", 0) == 1,
            QuietSounds = (Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes")?.GetValue("") as string) == ".None",
            DarkTaskManager = TaskManagerSettings().Long("AppTheme", 0) == 2
        };

        // Returns true when the taskbar needs a restart to show the change.
        public static bool ApplyTidy(Tidy t)
        {
            bool restart = false;
            if (SetDword(Personalize, "EnableTransparency", t.Transparency ? 1 : 0)) { Broadcast("ImmersiveColorSet"); Log.Info("transparency effects " + t.Transparency); }
            if (SetDword(@"Software\Microsoft\Windows\DWM", "ColorPrevalence", t.AccentTitleBars ? 1 : 0)) { Broadcast("ImmersiveColorSet"); Log.Info("accent on title bars " + t.AccentTitleBars); }
            if (SetDword(Adv, "HideIcons", t.HideDesktopIcons ? 1 : 0)) { restart = true; Log.Info("desktop icons hidden " + t.HideDesktopIcons); }
            if (t.HideTaskbarExtras)
            {
                bool c = SetDword(Adv, "ShowTaskViewButton", 0) | SetDword(Adv, "ShowCopilotButton", 0) | SetDword(Adv, "TaskbarMn", 0)
                       | SetDword(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0);
                if (c) { restart = true; Log.Info("taskbar extras hidden"); }
            }
            if (t.StartNoRecent)
            {
                bool c = SetDword(Adv, "Start_TrackDocs", 0) | SetDword(Adv, "Start_IrisRecommendations", 0) | SetDword(Adv, "Start_AccountNotifications", 0);
                if (c) Log.Info("Start: no recent files or recommendations");
            }
            // the Dock wants: bottom, normal-size buttons, main screen only
            if (SetDword(Adv, "TaskbarSmallIcons", 0) | SetDword(Adv, "MMTaskbarEnabled", 0)) restart = true;
            if (t.QuietSounds) QuietSounds();
            SetTaskManagerDark(t.DarkTaskManager);
            return restart;
        }

        // ---------- Task Manager's own app theme ----------
        // Settings > App theme in Task Manager writes AppTheme 2 (dark) and CustomizeMode 2 to this file; it reads it
        // when it opens and writes it back when it closes, so it's only changed while Task Manager is closed.

        static string TaskManagerSettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\TaskManager\settings.json");

        static Dictionary<string, object> TaskManagerSettings()
        {
            try { return File.Exists(TaskManagerSettingsFile) ? Json.ParseObject(File.ReadAllText(TaskManagerSettingsFile)) : new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        static void SetTaskManagerDark(bool dark)
        {
            var s = TaskManagerSettings();
            if ((s.Long("AppTheme", 0) == 2 && s.Long("CustomizeMode", 0) == 2) == dark) return;
            if (Process.GetProcessesByName("Taskmgr").Length > 0) { Log.Warn("Task Manager is open, so its theme wasn't changed: close it and use Repair"); return; }
            if (dark) { s["AppTheme"] = 2L; s["CustomizeMode"] = 2L; } else { s.Remove("AppTheme"); s.Remove("CustomizeMode"); }
            Directory.CreateDirectory(Path.GetDirectoryName(TaskManagerSettingsFile));
            File.WriteAllText(TaskManagerSettingsFile, Json.Write(s));
            Log.Info("Task Manager " + (dark ? "dark" : "follows Windows' light and dark"));
        }

        static void QuietSounds()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"AppEvents\Schemes"))
            {
                if ((k.GetValue("") as string) == ".None") return;
                Backup.Save(@"HKCU\AppEvents\Schemes");
                k.SetValue("", ".None");
            }
            using (var apps = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps"))
                if (apps != null)
                    foreach (var app in apps.GetSubKeyNames())
                        using (var a = apps.OpenSubKey(app))
                            foreach (var ev in a.GetSubKeyNames())
                                using (var cur = Registry.CurrentUser.OpenSubKey($@"AppEvents\Schemes\Apps\{app}\{ev}\.Current", true))
                                    cur?.SetValue("", "");
            Log.Info("system sounds off");
        }

        // ---------- Start's phone panel (a per-computer extra, for PCs that never pair a phone) ----------

        const string PhoneKey = @"Software\Microsoft\Windows\CurrentVersion\Start\Companions\Microsoft.YourPhone_8wekyb3d8bbwe";
        public static bool PhonePanelHidden => GetDword(PhoneKey, "IsEnabled", 1) == 0;
        public static void SetPhonePanelHidden(bool hidden)
        {
            if (Registry.CurrentUser.OpenSubKey(PhoneKey) == null && !hidden) return;
            if (SetDword(PhoneKey, "IsEnabled", hidden ? 0 : 1)) Log.Info("Start phone panel " + (hidden ? "hidden" : "shown"));
        }

        // ---------- restarting the taskbar ----------

        // Ends explorer and lets Windows start it again itself, which brings it back as the normal user even when
        // Glasshouse is running as admin (an admin explorer misbehaves). Open folder windows close.
        public static void RestartExplorer()
        {
            if (!HasDesktop) { Log.Warn("no desktop here: taskbar not restarted"); return; }
            Log.Info("restarting the taskbar (explorer)");
            foreach (var p in Process.GetProcessesByName("explorer")) try { p.Kill(); } catch { }
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(250);
                if (FindWindow("Shell_TrayWnd", null) != IntPtr.Zero) { Thread.Sleep(1500); return; }
            }
            Log.Warn("explorer didn't come back by itself; starting it");
            AsUser("explorer.exe", "", null);
            Thread.Sleep(3000);
        }

        // ---------- launching programs as the normal user ----------

        // From an admin process, a program started directly would run as admin too (PowerToys, the wallpaper app and
        // Tailscale's tray shouldn't). Explorer starts things with the user's normal rights, so go through it,
        // via a shortcut when a working folder or arguments are needed.
        public static void AsUser(string exe, string args, string workDir)
        {
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo(exe, args ?? "") { UseShellExecute = true, WorkingDirectory = workDir ?? Path.GetDirectoryName(exe) ?? "" });
                return;
            }
            string target = exe;
            if (!string.IsNullOrEmpty(args) || !string.IsNullOrEmpty(workDir))
            {
                var dir = Path.Combine(Log.Dir, "launch");
                Directory.CreateDirectory(dir);
                target = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + ".lnk");
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic lnk = sh.CreateShortcut(target);
                lnk.TargetPath = exe; lnk.Arguments = args ?? ""; lnk.WorkingDirectory = workDir ?? Path.GetDirectoryName(exe); lnk.WindowStyle = 7;
                lnk.Save();
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"") { UseShellExecute = false });
        }

        // Runs a console program with no window at all and returns its output.
        public static (int code, string output) Run(string exe, string args, int timeoutSec = 600)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } return (-1, "timed out"); }
                return (p.ExitCode, (outTask.Result + errTask.Result).Trim());
            }
        }

        public static bool Running(string processName) => Process.GetProcessesByName(processName).Length > 0;

        public static void StopProcess(string processName, int waitMs = 5000)
        {
            foreach (var p in Process.GetProcessesByName(processName)) try { p.Kill(); p.WaitForExit(waitMs); } catch { }
        }
    }

    // Before Glasshouse changes something it didn't make, the old value is exported once (never overwritten), so it
    // can be put back by hand: %LOCALAPPDATA%\Glasshouse\backup\*.reg
    public static class Backup
    {
        public static string Dir => Path.Combine(Log.Dir, "backup");
        public static void Save(string regKey)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var f = Path.Combine(Dir, regKey.Replace('\\', '_').Replace(' ', '_') + ".reg");
                if (!File.Exists(f)) Shell.Run("reg.exe", $"export \"{regKey}\" \"{f}\" /y", 30);
            }
            catch (Exception e) { Log.Warn("backup " + regKey + ": " + e.Message); }
        }

        public static void SaveFile(string path)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var f = Path.Combine(Dir, Path.GetFileName(path) + ".orig");
                if (File.Exists(path) && !File.Exists(f)) File.Copy(path, f);
            }
            catch (Exception e) { Log.Warn("backup " + path + ": " + e.Message); }
        }
    }
}
