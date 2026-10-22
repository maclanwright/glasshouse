using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Glasshouse
{
    // A piece of admin work (Windhawk's settings live in HKLM). Written to a file, done by an admin copy of Glasshouse:
    //  - after setup: the "Glasshouse helper" task (registered once, runs with highest rights as this user, so no
    //    UAC prompt for every slider move);
    //  - before setup, or if the task is missing: Windows' normal admin prompt (UAC), once;
    //  - when Glasshouse itself already runs as admin: right here.
    public class Job
    {
        public string Kind;                 // windhawk.apply | setup.admin
        public List<ModSpec> Mods = new List<ModSpec>();
        public bool InstallWindhawk, InstallFileConverter, InstallSelf;
        public string Id = Guid.NewGuid().ToString("N");

        public string ToJsonText() => Json.Write(new Dictionary<string, object>
        {
            ["id"] = Id, ["kind"] = Kind, ["installWindhawk"] = InstallWindhawk, ["installFileConverter"] = InstallFileConverter,
            ["installSelf"] = InstallSelf, ["mods"] = Mods.Select(m => (object)m.ToJson()).ToList()
        });

        public static Job FromJsonText(string t)
        {
            var d = Json.ParseObject(t);
            return new Job
            {
                Id = d.Str("id"), Kind = d.Str("kind"), InstallWindhawk = d.Bool("installWindhawk"), InstallFileConverter = d.Bool("installFileConverter"),
                InstallSelf = d.Bool("installSelf"), Mods = (d.Arr("mods") ?? new List<object>()).Select(m => ModSpec.FromJson((Dictionary<string, object>)m)).ToList()
            };
        }
    }

    public class JobResult
    {
        public bool Ok; public string Error = "";
        public List<string> Changed = new List<string>(), Log = new List<string>();
        public bool ServicesRestarted;

        public string ToJsonText() => Json.Write(new Dictionary<string, object>
        {
            ["ok"] = Ok, ["error"] = Error, ["changed"] = Changed.Cast<object>().ToList(), ["log"] = Log.Cast<object>().ToList(), ["servicesRestarted"] = ServicesRestarted
        });
        public static JobResult FromJsonText(string t)
        {
            var d = Json.ParseObject(t);
            return new JobResult
            {
                Ok = d.Bool("ok"), Error = d.Str("error", ""), ServicesRestarted = d.Bool("servicesRestarted"),
                Changed = (d.Arr("changed") ?? new List<object>()).Select(x => x.ToString()).ToList(),
                Log = (d.Arr("log") ?? new List<object>()).Select(x => x.ToString()).ToList()
            };
        }
    }

    public static class Elevation
    {
        public const string TaskName = "Glasshouse helper";
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Glasshouse");
        public static string InstalledExe => Path.Combine(InstallDir, "Glasshouse.exe");
        static string JobDir => Path.Combine(Log.Dir, "jobs");

        public static bool HelperReady
        {
            get
            {
                if (!File.Exists(InstalledExe)) return false;
                try { dynamic svc = Scheduler(); return svc.GetFolder("\\").GetTask(TaskName) != null; } catch { return false; }
            }
        }

        static dynamic Scheduler()
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            return svc;
        }

        public static JobResult Run(Job job)
        {
            if (Shell.IsElevated) return Do(job, trusted: true);
            Directory.CreateDirectory(JobDir);
            var jobFile = Path.Combine(JobDir, $"job-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{job.Id}.json");
            var resFile = Path.Combine(JobDir, $"result-{job.Id}.json");
            File.WriteAllText(jobFile, job.ToJsonText(), new UTF8Encoding(false));
            bool viaTask = job.Kind == "windhawk.apply" && HelperReady;
            if (viaTask)
            {
                Log.Info("asking the Glasshouse helper (admin) to make the change");
                dynamic svc = Scheduler();
                svc.GetFolder("\\").GetTask(TaskName).Run(null);
            }
            else
            {
                Log.Info("Windows will ask for admin permission once");
                try
                {
                    var p = Process.Start(new ProcessStartInfo(Program.SelfPath, $"--elevated-job \"{jobFile}\"") { Verb = "runas", UseShellExecute = true });
                    p.WaitForExit();
                }
                catch (System.ComponentModel.Win32Exception) { TryDelete(jobFile); return new JobResult { Error = "admin permission wasn't given" }; }
            }
            // the task runs on its own: wait for its answer
            var until = DateTime.Now.AddMinutes(job.Kind == "setup.admin" ? 30 : 3);
            while (!File.Exists(resFile) && DateTime.Now < until) Thread.Sleep(250);
            if (!File.Exists(resFile)) { TryDelete(jobFile); return new JobResult { Error = "the admin helper didn't answer" }; }
            Thread.Sleep(100);
            var res = JobResult.FromJsonText(File.ReadAllText(resFile));
            TryDelete(resFile);
            foreach (var l in res.Log) Log.Info("  [admin] " + l);
            return res;
        }

        static void TryDelete(string f) { try { File.Delete(f); } catch { } }

        // --helper: the scheduled task. Does every waiting job, oldest first. Jobs here can only change Windhawk
        // mods: installing programs or replacing Glasshouse itself needs the real admin prompt.
        public static void HelperMain()
        {
            if (!Directory.Exists(JobDir)) return;
            foreach (var f in Directory.GetFiles(JobDir, "job-*.json").OrderBy(f => f))
            {
                Job job;
                bool stale = File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-10);   // nobody is waiting for it any more
                try { job = Job.FromJsonText(File.ReadAllText(f)); } catch (Exception e) { Log.Error("bad job file: " + e.Message); TryDelete(f); continue; }
                TryDelete(f);
                if (stale) continue;
                var res = Do(job, trusted: false);
                File.WriteAllText(Path.Combine(JobDir, $"result-{job.Id}.json"), res.ToJsonText(), new UTF8Encoding(false));
            }
        }

        // --elevated-job <file>: started through the UAC prompt.
        public static void ElevatedJobMain(string jobFile)
        {
            var job = Job.FromJsonText(File.ReadAllText(jobFile));
            TryDelete(jobFile);
            var res = Do(job, trusted: true);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(jobFile), $"result-{job.Id}.json"), res.ToJsonText(), new UTF8Encoding(false));
        }

        static JobResult Do(Job job, bool trusted)
        {
            var res = new JobResult();
            Action<string> grab = l => res.Log.Add(l);
            Glasshouse.Log.Line += grab;
            try
            {
                if (!Shell.IsElevated) throw new UnauthorizedAccessException("not running as admin");
                switch (job.Kind)
                {
                    case "windhawk.apply":
                        foreach (var m in job.Mods)
                            if (Windhawk.LoadsEverywhere(m.Include) && m.Id != Windhawk.GlassModId && Windhawk.Read(m.Id) == null)
                                throw new InvalidOperationException($"{m.Id} would load into every program; new mods must name the programs they load into");
                        var r = Windhawk.Apply(job.Mods);
                        res.Changed = r.Changed; res.ServicesRestarted = r.ServicesRestarted;
                        break;
                    case "setup.admin":
                        if (!trusted) throw new UnauthorizedAccessException("setup needs Windows' admin prompt");
                        SetupAdmin(job, res);
                        break;
                    default: throw new InvalidOperationException("unknown job " + job.Kind);
                }
                res.Ok = true;
            }
            catch (Exception e) { res.Error = e.Message; Glasshouse.Log.Error(e.ToString()); }
            finally { Glasshouse.Log.Line -= grab; }
            return res;
        }

        // The admin half of Set up: Windhawk itself, File Converter (a machine-wide installer), the mods, and Glasshouse
        // into Program Files with its helper task and Start menu entry.
        static void SetupAdmin(Job job, JobResult res)
        {
            if (job.InstallWindhawk && !Windhawk.IsInstalled)
            {
                if (!Winget.Install("RamenSoftware.Windhawk", scope: "machine") && !Windhawk.IsInstalled) throw new InvalidOperationException("Windhawk didn't install");
                for (int i = 0; i < 40 && !Windhawk.ServiceRunning; i++) Thread.Sleep(500);
                res.Changed.Add("Windhawk");
            }
            if (job.InstallFileConverter && !AppPaths.FileConverterInstalled)
                if (Winget.Install("AdrienAllard.FileConverter")) res.Changed.Add("File Converter");
            if (job.Mods.Count > 0)
            {
                var r = Windhawk.Apply(job.Mods);
                res.Changed.AddRange(r.Changed); res.ServicesRestarted |= r.ServicesRestarted;
            }
            if (job.InstallSelf) InstallSelf();
        }

        static void InstallSelf()
        {
            Directory.CreateDirectory(InstallDir);
            if (!string.Equals(Path.GetFullPath(Program.SelfPath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
            {
                // a running copy can't be overwritten, but it can be renamed out of the way
                if (File.Exists(InstalledExe)) { var old = InstalledExe + ".old"; TryDelete(old); try { File.Move(InstalledExe, old); } catch { } }
                File.Copy(Program.SelfPath, InstalledExe, true);
            }
            var user = WindowsIdentity.GetCurrent().Name;
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Lets Glasshouse change Windhawk's mod settings without asking for admin each time. Only changes mods; started by Glasshouse.</Description></RegistrationInfo>
  <Principals><Principal id=""Author""><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled><Hidden>false</Hidden><ExecutionTimeLimit>PT30M</ExecutionTimeLimit><Priority>5</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>""{InstalledExe}""</Command><Arguments>--helper</Arguments></Exec></Actions>
</Task>";
            var xf = Path.Combine(Path.GetTempPath(), "glasshouse-task.xml");
            File.WriteAllText(xf, xml, Encoding.Unicode);
            var (code, output) = Shell.Run("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{xf}\" /F", 60);
            TryDelete(xf);
            if (code != 0) throw new InvalidOperationException("couldn't register the helper task: " + output);
            Log.Info("Glasshouse installed in " + InstallDir + " with its helper task");
            // Start menu entry for everyone
            try
            {
                var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Glasshouse.lnk");
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic l = sh.CreateShortcut(lnk);
                l.TargetPath = InstalledExe; l.WorkingDirectory = InstallDir; l.Description = "Glasshouse"; l.Save();
            }
            catch (Exception e) { Log.Warn("Start menu entry: " + e.Message); }
            // listed in Settings > Apps, so it can be removed like anything else
            using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Glasshouse"))
            {
                k.SetValue("DisplayName", "Glasshouse");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "maclanwright");
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }

        // Removes Glasshouse itself (its folder, task, Start menu entry). The look stays: Windhawk and the apps
        // are separate programs, each removable in Settings > Apps.
        public static void Uninstall()
        {
            Shell.Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", 30);
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Glasshouse.lnk"));
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Glasshouse", false);
            // the running exe can't delete itself: a hidden cmd does it a moment later
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rd /s /q \"{InstallDir}\"") { CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
