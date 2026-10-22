using System;
using System.Collections.Generic;
using System.IO;

namespace Glasshouse
{
    // One log file per user (%LOCALAPPDATA%\Glasshouse\glasshouse.log), plus anyone listening (the window, the console).
    public static class Log
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glasshouse");
        public static string FilePath => Path.Combine(Dir, "glasshouse.log");
        public static event Action<string> Line;
        static readonly object Gate = new object();
        static readonly int Pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        public static readonly List<string> Session = new List<string>();

        public static void Info(string m) => Write(m);
        public static void Warn(string m) => Write("WARNING: " + m);
        public static void Error(string m) => Write("ERROR: " + m);

        static void Write(string m)
        {
            lock (Gate)
            {
                Session.Add(m);
                try
                {
                    Directory.CreateDirectory(Dir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2_000_000)   // keep it small: one old copy
                    {
                        File.Delete(FilePath + ".old");
                        File.Move(FilePath, FilePath + ".old");
                    }
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Pid}] {m}\r\n");
                }
                catch { }
            }
            try { Line?.Invoke(m); } catch { }
        }
    }
}
