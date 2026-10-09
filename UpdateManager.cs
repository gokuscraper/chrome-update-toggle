using System.Diagnostics;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace ChromeUpdateToggle;

/// <summary>基线快照: 禁止更新前记录机器原始状态, 恢复时照此还原。</summary>
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

    // ---------- 查询 ----------

    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 本机实际存在的 Google 更新服务(只含 Updater 系, Elevation excluded)。
    /// 双保险: 服务名命中模式 且 可执行路径含 Google, 缺一不要, 防第三方撞名误伤。
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

    /// <summary>本机实际存在的 Google 更新类计划任务(只含 TaskName 含 Google+Update 的)。</summary>
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

    /// <summary>本机实际存在的更新主程序: legacy + 各版本 updater.exe。</summary>
    public static List<string> FindUpdaterExes()
    {
        var list = new List<string>();
        if (File.Exists(LegacyExe)) list.Add(LegacyExe);
        if (Directory.Exists(GoogleUpdaterDir))
        {
            list.AddRange(Directory.GetFiles(GoogleUpdaterDir, "updater.exe",
                SearchOption.AllDirectories));
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
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
        sw.AppendLine($"总体: {status} ({detail})");
        foreach (var s in FindUpdaterServices())
            sw.AppendLine($"服务 {s} = {Safe(() => GetServiceStartMode(s))}");
        sw.AppendLine($"服务 GoogleChromeElevationService = {GetElevationStartMode()} (保持不动)");
        var tasks = FindUpdaterTasks();
        sw.AppendLine(tasks.Count == 0 ? "任务 (无Google更新类任务)" :
            string.Join(" | ", tasks.Select(t => $"{t.FullName}={t.State}")));
        sw.AppendLine($"注册表 UpdateDefault = {GetUpdateDefault()?.ToString() ?? "(无策略键)"}");
        var files = FindUpdaterExes();
        sw.AppendLine(files.Count == 0 ? "文件 (无更新主程序)" :
            string.Join(" | ", files.Select(f => $"{Path.GetFileName(f)} DENY={FileHasDeny(f)}")));
        return sw.ToString();
    }

    /// <summary>综合三支柱判定: 已禁止更新 / 更新正常 / 状态不一致。</summary>
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
            return ("已禁止更新", "服务全Disabled + 策略UpdateDefault=0 + 主程序全锁定");
        if (svcAuto && regOk && lockNone)
            return ("更新正常", "服务全Auto + 无禁用策略 + 主程序无锁定");
        var parts = new List<string>
        {
            "服务:" + (svcDis ? "已禁" : svcAuto ? "自动" : "混合"),
            "策略:" + (regDis ? "已禁" : regOk ? "正常" : $"UpdateDefault={ud}"),
            "锁定:" + (lockAll ? "全锁" : lockNone ? "无锁" : "部分锁"),
        };
        return ("状态不一致", string.Join(" ", parts));
    }

    // ---------- 禁止 ----------

    public static Baseline Disable(Action<string> log)
    {
        var base_ = CaptureBaseline(log);
        string dir = SaveBaseline(base_, "disable");
        log($"基线已存: {dir}");

        log("[1/5] 结束 Chrome/更新进程");
        KillProcess("chrome");
        KillProcess("GoogleUpdate");
        KillProcess("updater");

        log("[2/5] 禁用更新服务 (Elevation 不动)");
        foreach (var s in FindUpdaterServices())
        {
            TryStopService(s);
            SetStartMode(s, "Disabled");
            log($"  {s} -> Disabled");
        }

        log("[3/5] 禁用计划任务 (无则跳过)");
        TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineCore\" /Disable");
        TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineUA\" /Disable");
        var tasks = FindUpdaterTasks();
        if (tasks.Count == 0) log("  (本机无 Google 更新类任务, SKIP)");
        foreach (var t in tasks)
        {
            Run("schtasks", $"/Change /TN \"{t.FullName}\" /Disable");
            log($"  {t.FullName} -> Disabled");
        }

        log("[4/5] 写注册表策略 (官方 kill-switch)");
        using (var key = Registry.LocalMachine.CreateSubKey(RegPath))
        {
            key!.SetValue("UpdateDefault", 0, RegistryValueKind.DWord);
            key.SetValue("DisableAutoUpdateChecksCheckboxValue", 1, RegistryValueKind.DWord);
            key.SetValue("AutoUpdateCheckPeriodMinutes", 0, RegistryValueKind.DWord);
            key.SetValue($"Update{ChromeGuid}", 0, RegistryValueKind.DWord);
        }
        log("  UpdateDefault=0");

        log("[5/5] 锁定更新主程序 (Deny 执行+写)");
        var files = FindUpdaterExes();
        if (files.Count == 0) log("  (无更新主程序, SKIP)");
        foreach (var f in files)
        {
            AddDeny(f);
            log($"  LOCKED: {f}");
        }

        log("[OK] 已禁止更新。chrome://settings/help 应显示由组织管理/无法更新。");
        return base_;
    }

    // ---------- 恢复(按最新基线) ----------

    public static void Enable(Action<string> log)
    {
        var dir = NewestBaselineDir();
        Baseline? base_ = dir != null ? LoadBaseline(dir) : null;
        if (base_ != null) log($"使用基线: {dir}");
        else log("(无基线, 用默认策略恢复)");

        log("[1/4] 解锁更新主程序");
        foreach (var f in FindUpdaterExes())
        {
            RemoveDeny(f);
            log($"  UNLOCKED: {f}");
        }

        log("[2/4] 还原服务 (Elevation 从未动过)");
        if (base_ != null)
        {
            foreach (var s in base_.Services)
            {
                try
                {
                    SetStartMode(s.Name, s.StartMode);
                    log($"  {s.Name} -> {s.StartMode} (基线)");
                }
                catch (Exception ex)
                {
                    log($"  [SKIP] {s.Name} 不存在或改不动: {ex.Message}");
                }
            }
        }
        else
        {
            // 无基线=干净机器, 出厂默认就是 Auto, 回 Auto 才能落到"更新正常"
            foreach (var s in FindUpdaterServices())
            {
                SetStartMode(s, "Auto");
                log($"  {s} -> Auto (默认)");
            }
        }

        log("[3/4] 还原计划任务");
        if (base_ != null && base_.Tasks.Count > 0)
        {
            foreach (var t in base_.Tasks)
            {
                if (t.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                    Run("schtasks", $"/Change /TN \"{t.FullName}\" /Disable");
                else
                    Run("schtasks", $"/Change /TN \"{t.FullName}\" /Enable");
                log($"  {t.FullName} -> {t.State} (基线)");
            }
        }
        else
        {
            TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineCore\" /Enable");
            TryRun("schtasks", "/Change /TN \"GoogleUpdateTaskMachineUA\" /Enable");
            foreach (var t in FindUpdaterTasks())
            {
                Run("schtasks", $"/Change /TN \"{t.FullName}\" /Enable");
                log($"  {t.FullName} -> Enabled");
            }
        }

        log("[4/4] 还原注册表");
        if (base_ != null)
        {
            if (base_.RegExisted)
            {
                Registry.LocalMachine.DeleteSubKeyTree(RegPath, false);
                using var key = Registry.LocalMachine.CreateSubKey(RegPath);
                foreach (var kv in base_.RegValues)
                    key!.SetValue(kv.Key, kv.Value, RegistryValueKind.DWord);
                log("  已按基线恢复策略值");
            }
            else
            {
                Registry.LocalMachine.DeleteSubKeyTree(RegPath, false);
                log("  已删除测试创建的策略键 (基线本无)");
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
            log("  UpdateDefault=1 (默认), 残留已清");
        }

        log("[OK] 已恢复更新。去 chrome://settings/help 点检查更新验证。");
    }

    /// <summary>恢复出厂默认: 服务 Auto / 任务启用 / 策略键删除 / 文件解锁。</summary>
    public static void ResetToDefaults(Action<string> log)
    {
        log("[1/4] 解锁更新主程序");
        foreach (var f in FindUpdaterExes())
        {
            RemoveDeny(f);
            log($"  UNLOCKED: {f}");
        }
        log("[2/4] 服务回 Auto");
        foreach (var s in FindUpdaterServices())
        {
            TryStopService(s);
            SetStartMode(s, "Auto");
            log($"  {s} -> Auto");
        }
        log("[3/4] 任务回启用");
        foreach (var t in FindUpdaterTasks())
        {
            Run("schtasks", $"/Change /TN \"{t.FullName}\" /Enable");
            log($"  {t.FullName} -> Enabled");
        }
        log("[4/4] 删除策略键");
        Registry.LocalMachine.DeleteSubKeyTree(RegPath, false);
        log("  已删除 HKLM\\" + RegPath);
        log("[OK] 已回到出厂默认 (Auto/Ready/无策略)。");
    }

    // ---------- 基线 ----------

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
        log($"基线: 服务{base_.Services.Count} 任务{base_.Tasks.Count} 注册表存在={base_.RegExisted} 文件{base_.Files.Count} Elevation={base_.ElevationStartMode}");
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
            ?? throw new InvalidOperationException("基线文件损坏: " + dir);
    }

    // ---------- 底层 ----------

    private static void SetStartMode(string service, string mode)
    {
        // WMI ChangeStartMode 只认 Automatic/Manual/Disabled, 基线里存的是 Auto
        string wmiMode = mode.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "Automatic" : mode;
        using var mo = new ManagementObject($"Win32_Service.Name='{service}'");
        var rc = Convert.ToUInt32(mo.InvokeMethod("ChangeStartMode", new object[] { wmiMode }));
        if (rc != 0) throw new InvalidOperationException($"服务 {service} 改为 {mode} 失败 (rc={rc})");
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
        catch { /* 已停或无权限则忽略, 外层会抛错 */ }
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
            throw new InvalidOperationException($"{exe} {args} 失败 exit={p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}");
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
