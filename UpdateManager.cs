using System.Diagnostics;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace ChromeUpdateToggle;

/// <summary>Baseline snapshot: records machine state before disabling, restores from it.</summary>
public sealed class Baseline
{
    public string Timestamp { get; set; } = "";
    public List<ServiceEntry> Services { get; set; } = new();
    public List<TaskEntry> Tasks { get; set; } = new();
    public bool RegExisted { get; set; }
    public Dictionary<string, int> RegValues { get; set; } = new();
    public List<string> Files { get; set; } = new();
    public string ElevationStartMode { get; set; } = "";
}

public sealed class ServiceEntry
{
    public string Name { get; set; } = "";
    public string StartMode { get; set; } = ""; // Auto / Manual / Disabled
}

public sealed class TaskEntry
{
    public string FullName { get; set; } = "";
    public string State { get; set; } = ""; // Ready / Disabled / ...
}

public static class UpdateManager
{
    private const string RegPath = @"SOFTWARE\Policies\Google\Update";
    private const string ChromeGuid = "{8A69D345-D564-463C-AFF1-A69D9E530F96}";

    private static readonly string GoogleUpdaterDir =
        @"C:\Program Files (x86)\Google\GoogleUpdater";
    private static readonly string LegacyExe =
        @"C:\Program Files (x86)\Google\Update\GoogleUpdate.exe";

    private static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private const FileSystemRights LockRights =
        FileSystemRights.ExecuteFile | FileSystemRights.Write;

    public static string BaseDir => AppContext.BaseDirectory;
    public static string BaselineRoot => Path.Combine(BaseDir, "baseline");

    // ---------- queries ----------

    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Updater services on this machine (Updater family only, Elevation excluded).
    /// Double gate: name matches pattern AND binary path contains Google.
    /// </summary>
    public static List<string> FindUpdaterServices()
    {
        var result = new List<string>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PathName FROM Win32_Service");
        foreach (ManagementObject mo in searcher.Get())
        {
            string name = mo["Name"]?.ToString() ?? "";
            string path = mo["PathName"]?.ToString() ?? "";
            bool nameHit = name.Contains("gupdate", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("GoogleUpdater", StringComparison.OrdinalIgnoreCase);
            if (!nameHit) continue;
            if (!path.Contains("Google", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(name);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public static string GetServiceStartMode(string name)
    {
        using var mo = new ManagementObject($"Win32_Service.Name='{name}'");
        mo.Get();
        return mo["StartMode"]?.ToString() ?? "?";
    }

    public static string GetElevationStartMode()
    {
        try { return GetServiceStartMode("GoogleChromeElevationService"); }
        catch { return "?"; }
    }

    /// <summary>Updater scheduled tasks on this machine (TaskName contains Google+Update).</summary>
    public static List<TaskEntry> FindUpdaterTasks()
    {
        var result = new List<TaskEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string output = Run("schtasks", "/Query /FO LIST");
        string? current = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.StartsWith("TaskName:", StringComparison.OrdinalIgnoreCase))
            {
                current = line["TaskName:".Length..].Trim();
            }
            else if (line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase) && current != null)
            {
                var state = line["Status:".Length..].Trim();
                if (current.Contains("Google", StringComparison.OrdinalIgnoreCase)
                    && current.Contains("Update", StringComparison.OrdinalIgnoreCase)
                    && seen.Add(current))
                {
                    result.Add(new TaskEntry { FullName = current, State = state });
                }
                current = null;
            }
        }
        return result;
    }

    /// <summary>
    /// Updater executables on this machine.
    /// Primary: derive directories from Updater services' BinaryPath;
    /// fallback: hardcoded paths (legacy GoogleUpdate.exe has no service).
    /// </summary>
    public static List<string> FindUpdaterExes()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Primary: derive from service BinaryPath
        using (var searcher = new ManagementObjectSearcher(
            "SELECT PathName FROM Win32_Service"))
        {
            foreach (ManagementObject mo in searcher.Get())
            {
                string raw = mo["PathName"]?.ToString() ?? "";
                if (!raw.Contains("Google", StringComparison.OrdinalIgnoreCase)) continue;
                if (!raw.Contains("pdat", StringComparison.OrdinalIgnoreCase)) continue;
                string exe = ParseExeFromServicePath(raw);
                if (exe == "" || !File.Exists(exe)) continue;
                found.Add(exe);
                // exe usually at <root>\<ver>\updater.exe, enumerate siblings under root
                string? root = Directory.GetParent(Path.GetDirectoryName(exe)!)?.FullName;
                if (root != null && Directory.Exists(root))
                {
                    foreach (var f in Directory.GetFiles(root, "updater.exe",
                        SearchOption.AllDirectories))
                        found.Add(f);
                }
            }
        }

        // Fallback: hardcoded (legacy has no service to derive from)
        if (File.Exists(LegacyExe)) found.Add(LegacyExe);
        if (Directory.Exists(GoogleUpdaterDir))
        {
            foreach (var f in Directory.GetFiles(GoogleUpdaterDir, "updater.exe",
                SearchOption.AllDirectories))
                found.Add(f);
        }

        return found.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Pure function: strip exe path from a service PathName.
    /// Quoted -> inside quotes (paths with spaces/parens), unquoted -> first token.
    /// </summary>
    public static string ParseExeFromServicePath(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.StartsWith("\""))
        {
            int end = raw.IndexOf('"', 1);
            if (end > 1) return raw.Substring(1, end - 1);
            return "";
        }
        int sp = raw.IndexOf(' ');
        return sp < 0 ? raw : raw.Substring(0, sp);
    }

    public static int? GetUpdateDefault()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegPath);
        return key?.GetValue("UpdateDefault") as int?;
    }

    public static bool FileHasDeny(string path)
    {
        try
        {
            var acl = new FileInfo(path).GetAccessControl();
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Deny) continue;
                if (!rule.IdentityReference.Equals(SystemSid)
                    && !rule.IdentityReference.Equals(AdminsSid)) continue;
                if ((rule.FileSystemRights & LockRights) != 0) return true;
            }
            return false;
        }
        catch { return false; }
    }

    public static string DescribeState()
    {
        var (status, detail) = GetUpdateStatus();
        var sw = new System.Text.StringBuilder();
        sw.AppendLine(Strings.Overall(status, detail));
        foreach (var s in FindUpdaterServices())
            sw.AppendLine(Strings.SvcLine(s, Safe(() => GetServiceStartMode(s))));
        sw.AppendLine(Strings.SvcElev(GetElevationStartMode()));
        var tasks = FindUpdaterTasks();
        sw.AppendLine(tasks.Count == 0 ? Strings.NoTasks :
            string.Join(" | ", tasks.Select(t => $"{t.FullName}={t.State}")));
        sw.AppendLine(Strings.RegLine(GetUpdateDefault()?.ToString() ?? Strings.RegAbsent));
        var files = FindUpdaterExes();
        sw.AppendLine(files.Count == 0 ? Strings.NoFiles :
            string.Join(" | ", files.Select(f => $"{Path.GetFileName(f)} DENY={FileHasDeny(f)}")));
        return sw.ToString();
    }

    /// <summary>Three-pillar verdict.</summary>
    public static (string Status, string Detail) GetUpdateStatus()
    {
        var svcs = FindUpdaterServices();
        bool svcDis = svcs.Count > 0 && svcs.All(s => Safe(() => GetServiceStartMode(s)) == "Disabled");
        bool svcAuto = svcs.Count > 0 && svcs.All(s => Safe(() => GetServiceStartMode(s)) == "Auto");
        int? ud = null;
        try { ud = GetUpdateDefault(); } catch { }
        bool regDis = ud == 0;
        bool regOk = ud is null or 1;
        var files = FindUpdaterExes();
        bool lockAll = files.Count > 0 && files.All(FileHasDeny);
        bool lockNone = files.All(f => !FileHasDeny(f));

        if (svcDis && regDis && lockAll)
            return (Strings.StDisabled, Strings.StDisabledDetail);
        if (svcAuto && regOk && lockNone)
            return (Strings.StNormal, Strings.StNormalDetail);
        var parts = new List<string>
        {
            Strings.PillarSvc + (svcDis ? Strings.PillarSvcDis : svcAuto ? Strings.PillarSvcAuto : Strings.PillarSvcMixed),
            Strings.PillarReg + (regDis ? Strings.PillarSvcDis : regOk ? Strings.PillarRegOk : $"UpdateDefault={ud}"),
            Strings.PillarLock + (lockAll ? Strings.PillarLockAll : lockNone ? Strings.PillarLockNone : Strings.PillarLockPart),
        };
        return (Strings.StMixed, string.Join(" ", parts));
    }

    // ---------- disable ----------

    public static Baseline Disable(Action<string> log)
    {
        var base_ = CaptureBaseline(log);
        string dir = SaveBaseline(base_, "disable");
        log(Strings.BaseSaved(dir));

        log(Strings.StepKill);
        KillProcess("chrome");
        KillProcess("GoogleUpdate");
        KillProcess("updater");

        log(Strings.StepSvc);
        foreach (var s in FindUpdaterServices())
        {
            TryStopService(s);
            SetStartMode(s, "Disabled");
            log(Strings.ToDisabled(s));
        }

        log(Strings.StepTask);
        TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineCore\" /Disable");
        TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineUA\" /Disable");
        var tasks = FindUpdaterTasks();
        if (tasks.Count == 0) log(Strings.NoTaskSkip);
        foreach (var t in tasks)
        {
            Run("schtasks", $"/Change /TN \"{t.FullName}\" /Disable");
            log(Strings.ToDisabled(t.FullName));
        }

        log(Strings.StepReg);
        using (var key = Registry.LocalMachine.CreateSubKey(RegPath))
        {
            key!.SetValue("UpdateDefault", 0, RegistryValueKind.DWord);
            key.SetValue("DisableAutoUpdateChecksCheckboxValue", 1, RegistryValueKind.DWord);
            key.SetValue("AutoUpdateCheckPeriodMinutes", 0, RegistryValueKind.DWord);
            key.SetValue($"Update{ChromeGuid}", 0, RegistryValueKind.DWord);
        }
        log("  UpdateDefault=0");

        log(Strings.StepLock);
        var files = FindUpdaterExes();
        if (files.Count == 0) log(Strings.NoFileSkip);
        foreach (var f in files)
        {
            AddDeny(f);
            log(Strings.Locked(f));
        }

        log(Strings.DisableOk);
        return base_;
    }

    // ---------- enable (from newest baseline) ----------

    public static void Enable(Action<string> log)
    {
        var dir = NewestBaselineDir();
        Baseline? base_ = dir != null ? LoadBaseline(dir) : null;
        if (base_ != null) log(Strings.UseBase(dir!));
        else log(Strings.NoBase);

        log(Strings.StepUnlock);
        foreach (var f in FindUpdaterExes())
        {
            RemoveDeny(f);
            log(Strings.Unlocked(f));
        }

        log(Strings.StepSvcRestore);
        if (base_ != null)
        {
            foreach (var s in base_.Services)
            {
                try
                {
                    SetStartMode(s.Name, s.StartMode);
                    log(Strings.SvcRestored(s.Name, s.StartMode));
                }
                catch (Exception ex)
                {
                    log(Strings.SkipMissing(s.Name, ex.Message));
                }
            }
        }
        else
        {
            // No baseline = clean machine, factory default is Auto
            foreach (var s in FindUpdaterServices())
            {
                SetStartMode(s, "Auto");
                log(Strings.ToAuto(s));
            }
        }

        log(Strings.StepTaskRestore);
        if (base_ != null && base_.Tasks.Count > 0)
        {
            foreach (var t in base_.Tasks)
            {
                if (t.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                    Run("schtasks", $"/Change /TN \"{t.FullName}\" /Disable");
                else
                    Run("schtasks", $"/Change /TN \"{t.FullName}\" /Enable");
                log($"  {t.FullName} -> {t.State} (baseline)");
            }
        }
        else
        {
            TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineCore\" /Enable");
            TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineUA\" /Enable");
            foreach (var t in FindUpdaterTasks())
            {
                Run("schtasks", $"/Change /TN \"{t.FullName}\" /Enable");
                log(Strings.ToEnabled(t.FullName));
            }
        }

        log(Strings.StepRegRestore);
        if (base_ != null)
        {
            if (base_.RegExisted)
            {
                Registry.LocalMachine.DeleteSubKeyTree(RegPath, false);
                using var key = Registry.LocalMachine.CreateSubKey(RegPath);
                foreach (var kv in base_.RegValues)
                    key!.SetValue(kv.Key, kv.Value, RegistryValueKind.DWord);
                log(Strings.RegImported);
            }
            else
            {
                Registry.LocalMachine.DeleteSubKeyTree(RegPath, false);
                log(Strings.RegDeleted);
            }
        }
        else
        {
            using (var key = Registry.LocalMachine.CreateSubKey(RegPath))
            {
                key!.SetValue("UpdateDefault", 1, RegistryValueKind.DWord);
                key.DeleteValue($"Update{ChromeGuid}", false);
                key.DeleteValue("AutoUpdateCheckPeriodMinutes", false);
                key.DeleteValue("DisableAutoUpdateChecksCheckboxValue", false);
            }
            log(Strings.RegDefault);
        }

        log(Strings.EnableOk);
    }

    /// <summary>Factory defaults: services Auto / tasks enabled / policy key deleted / exes unlocked.</summary>
    public static void ResetToDefaults(Action<string> log)
    {
        log(Strings.StepUnlock);
        foreach (var f in FindUpdaterExes())
        {
            RemoveDeny(f);
            log(Strings.Unlocked(f));
        }
        log(Strings.StepSvcAuto);
        foreach (var s in FindUpdaterServices())
        {
            TryStopService(s);
            SetStartMode(s, "Auto");
            log(Strings.ToAuto(s));
        }
        log(Strings.StepTaskOn);
        foreach (var t in FindUpdaterTasks())
        {
            Run("schtasks", $"/Change /TN \"{t.FullName}\" /Enable");
            log(Strings.ToEnabled(t.FullName));
        }
        log(Strings.StepRegDel);
        Registry.LocalMachine.DeleteSubKeyTree(RegPath, false);
        log(Strings.RegKeyDeleted(@"HKLM\" + RegPath));
        log(Strings.ResetOk);
    }

    // ---------- baseline ----------

    public static Baseline CaptureBaseline(Action<string> log)
    {
        var base_ = new Baseline { Timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") };
        foreach (var s in FindUpdaterServices())
            base_.Services.Add(new ServiceEntry { Name = s, StartMode = Safe(() => GetServiceStartMode(s), "?") });
        base_.Tasks.AddRange(FindUpdaterTasks());
        using (var key = Registry.LocalMachine.OpenSubKey(RegPath))
        {
            base_.RegExisted = key != null;
            if (key != null)
                foreach (var name in key.GetValueNames())
                    if (key.GetValue(name) is int v) base_.RegValues[name] = v;
        }
        base_.Files.AddRange(FindUpdaterExes());
        base_.ElevationStartMode = GetElevationStartMode();
        log(Strings.BaseSummary(base_.Services.Count, base_.Tasks.Count, base_.RegExisted, base_.Files.Count, base_.ElevationStartMode));
        return base_;
    }

    public static string SaveBaseline(Baseline base_, string action)
    {
        Directory.CreateDirectory(BaselineRoot);
        string dir = Path.Combine(BaselineRoot, $"{base_.Timestamp}-{action}");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(base_, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, "baseline.json"), json);
        return dir;
    }

    public static string? NewestBaselineDir()
    {
        if (!Directory.Exists(BaselineRoot)) return null;
        return Directory.GetDirectories(BaselineRoot)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static Baseline LoadBaseline(string dir)
    {
        var json = File.ReadAllText(Path.Combine(dir, "baseline.json"));
        return JsonSerializer.Deserialize<Baseline>(json)
            ?? throw new InvalidOperationException(Strings.ErrBaseBroken(dir));
    }

    // ---------- internals ----------

    private static void SetStartMode(string service, string mode)
    {
        // WMI ChangeStartMode only accepts Automatic/Manual/Disabled, baseline stores Auto
        string wmiMode = mode.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "Automatic" : mode;
        using var mo = new ManagementObject($"Win32_Service.Name='{service}'");
        var rc = Convert.ToUInt32(mo.InvokeMethod("ChangeStartMode", new object[] { wmiMode }));
        if (rc != 0) throw new InvalidOperationException(Strings.ErrSvcMode(service, mode, rc));
    }

    private static void TryStopService(string service)
    {
        try
        {
            using var sc = new ServiceController(service);
            if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.Paused)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        }
        catch { /* already stopped or no rights, outer layer throws */ }
    }

    private static void KillProcess(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
    }

    private static void AddDeny(string path)
    {
        var info = new FileInfo(path);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(SystemSid, LockRights, AccessControlType.Deny));
        acl.AddAccessRule(new FileSystemAccessRule(AdminsSid, LockRights, AccessControlType.Deny));
        info.SetAccessControl(acl);
    }

    private static void RemoveDeny(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var acl = info.GetAccessControl();
            acl.RemoveAccessRule(new FileSystemAccessRule(SystemSid, LockRights, AccessControlType.Deny));
            acl.RemoveAccessRule(new FileSystemAccessRule(AdminsSid, LockRights, AccessControlType.Deny));
            info.SetAccessControl(acl);
        }
        catch { }
    }

    private static string Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        string out_ = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException(Strings.ErrProc(exe, args, p.ExitCode, p.StandardError.ReadToEnd().Trim()));
        return out_;
    }

    private static bool TryRun(string exe, string args)
    {
        try { Run(exe, args); return true; }
        catch { return false; }
    }

    private static string Safe(Func<string> f, string fallback = "?")
    {
        try { return f(); } catch { return fallback; }
    }
}
