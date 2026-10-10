namespace ChromeUpdateToggle;

/// <summary>中英双语字符串。默认中文, 切英文即时生效并持久化(exe旁 lang.txt)。</summary>
public static class Strings
{
    private static string _current = Load();

    public static string Current => _current;

    private static string Load()
    {
        try
        {
            string f = Path.Combine(AppContext.BaseDirectory, "lang.txt");
            if (File.Exists(f) && File.ReadAllText(f).Trim().ToLowerInvariant() == "en")
                return "en";
        }
        catch { }
        return "zh";
    }

    public static void SetLang(string lang)
    {
        _current = lang == "en" ? "en" : "zh";
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "lang.txt"), _current); }
        catch { }
    }

    private static string T(string zh, string en) => _current == "en" ? en : zh;

    // ---- 主窗体 ----
    public static string AppTitle => T($"Chrome 自动更新开关 v{Logger.Version}", $"Chrome Update Toggle v{Logger.Version}");
    public static string GroupOp => T("请选择操作", "Select action");
    public static string OpDisable => T("禁止更新", "Disable updates");
    public static string OpEnable => T("恢复更新", "Restore updates");
    public static string BtnOK => T("确定", "OK");
    public static string BtnRefresh => T("刷新状态", "Refresh");
    public static string BtnExport => T("导出诊断", "Export diag");
    public static string Author => T("作者：开源探长彪哥", "Author: gokuscraper");
    public const string RepoUrl = "https://github.com/gokuscraper/chrome-update-toggle";
    public static string LangZh => "中文";
    public static string LangEn => "English";
    public static string StartedSection(string title) => T($"===== {title} 开始 =====", $"===== {title} started =====");
    public static string DoneOk(string title) => T($"===== {title} 成功 =====", $"===== {title} succeeded =====");
    public static string DoneFail(string title, int rc) => T($"===== {title} 失败/取消 (exit={rc}) =====", $"===== {title} failed/cancelled (exit={rc}) =====");
    public static string NeedAdminUI => T("当前非管理员, 弹 UAC 提权执行 (点\"是\"即可)...", "Not admin, requesting elevation (click Yes)...");
    public static string HeadAdmin => T("[管理员] ", "[Admin] ");
    public static string HeadNonAdmin => T("[非管理员, 只能查看] ", "[Non-admin, view only] ");
    public static string StateNow(string s, string d) => T($"当前: {s} ({d})", $"Current: {s} ({d})");
    public static string StateReadFail => T("读取状态失败: ", "Failed to read state: ");
    public static string Checking => T("检测中…", "Checking…");
    public static string CheckingDetail => T("正在检测当前状态，请稍候…", "Detecting current state, please wait…");
    public static string ZipFilter => T("ZIP 压缩包|*.zip", "ZIP Archive|*.zip");
    public static string DiagDefaultName => $"ChromeUpdateToggle-{(Current == "en" ? "diag" : "诊断")}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    // ---- 总体状态 ----
    public static string StDisabled => T("已禁止更新", "Updates DISABLED");
    public static string StNormal => T("更新正常", "Updates NORMAL");
    public static string StMixed => T("状态不一致", "INCONSISTENT STATE");
    public static string StDisabledDetail => T("服务全Disabled + 策略UpdateDefault=0 + 主程序全锁定", "services all Disabled + policy UpdateDefault=0 + exes all locked");
    public static string StNormalDetail => T("服务全Auto + 无禁用策略 + 主程序无锁定", "services all Auto + no blocking policy + exes unlocked");
    public static string PillarSvc => T("服务:", "services:");
    public static string PillarSvcDis => T("已禁", "disabled");
    public static string PillarSvcAuto => T("自动", "auto");
    public static string PillarSvcMixed => T("混合", "mixed");
    public static string PillarReg => T("策略:", "policy:");
    public static string PillarRegOk => T("正常", "ok");
    public static string PillarLock => T("锁定:", "lock:");
    public static string PillarLockAll => T("全锁", "all locked");
    public static string PillarLockNone => T("无锁", "unlocked");
    public static string PillarLockPart => T("部分锁", "partial");

    // ---- 状态明细 ----
    public static string Overall(string s, string d) => T($"总体: {s} ({d})", $"Overall: {s} ({d})");
    public static string SvcLine(string s, string m) => T($"服务 {s} = {m}", $"Service {s} = {m}");
    public static string SvcElev(string m) => T($"服务 GoogleChromeElevationService = {m} (保持不动)", $"Service GoogleChromeElevationService = {m} (untouched)");
    public static string NoTasks => T("任务 (无Google更新类任务)", "Tasks (no Google updater tasks)");
    public static string RegLine(string v) => T($"注册表 UpdateDefault = {v}", $"Registry UpdateDefault = {v}");
    public static string RegAbsent => T("(无策略键)", "(no policy key)");
    public static string NoFiles => T("文件 (无更新主程序)", "Files (no updater exes)");

    // ---- 禁止流程 ----
    public static string BaseSaved(string dir) => T($"基线已存: {dir}", $"Baseline saved: {dir}");
    public static string StepKill => T("[1/5] 结束 Chrome/更新进程", "[1/5] Kill Chrome/updater processes");
    public static string StepSvc => T("[2/5] 禁用更新服务 (Elevation 不动)", "[2/5] Disable updater services (Elevation untouched)");
    public static string ToDisabled(string s) => T($"  {s} -> Disabled", $"  {s} -> Disabled");
    public static string StepTask => T("[3/5] 禁用计划任务 (无则跳过)", "[3/5] Disable scheduled tasks (skip if none)");
    public static string NoTaskSkip => T("  (本机无 Google 更新类任务, SKIP)", "  (no Google updater tasks on this machine, SKIP)");
    public static string StepReg => T("[4/5] 写注册表策略 (官方 kill-switch)", "[4/5] Write registry policy (official kill-switch)");
    public static string StepLock => T("[5/5] 锁定更新主程序 (Deny 执行+写)", "[5/5] Lock updater exes (Deny execute+write)");
    public static string NoFileSkip => T("  (无更新主程序, SKIP)", "  (no updater exes, SKIP)");
    public static string Locked(string f) => T($"  LOCKED: {f}", $"  LOCKED: {f}");
    public static string DisableOk => T("[OK] 已禁止更新。chrome://settings/help 应显示由组织管理/无法更新。",
        "[OK] Updates disabled. chrome://settings/help should show managed / cannot update.");
    public static string BaseSummary(int s, int t, bool r, int f, string e) =>
        T($"基线: 服务{s} 任务{t} 注册表存在={r} 文件{f} Elevation={e}",
          $"Baseline: {s} services, {t} tasks, regExists={r}, {f} files, Elevation={e}");

    // ---- 恢复流程 ----
    public static string UseBase(string d) => T($"使用基线: {d}", $"Using baseline: {d}");
    public static string NoBase => T("(无基线, 用默认策略恢复)", "(no baseline, restore with defaults)");
    public static string StepUnlock => T("[1/4] 解锁更新主程序", "[1/4] Unlock updater exes");
    public static string Unlocked(string f) => T($"  UNLOCKED: {f}", $"  UNLOCKED: {f}");
    public static string StepSvcRestore => T("[2/4] 还原服务 (Elevation 从未动过)", "[2/4] Restore services (Elevation never touched)");
    public static string SvcRestored(string n, string m) => T($"  {n} -> {m} (基线)", $"  {n} -> {m} (baseline)");
    public static string SkipMissing(string n, string e) => T($"  [SKIP] {n} 不存在或改不动: {e}", $"  [SKIP] {n} missing or not changeable: {e}");
    public static string StepTaskRestore => T("[3/4] 还原计划任务", "[3/4] Restore scheduled tasks");
    public static string StepRegRestore => T("[4/4] 还原注册表", "[4/4] Restore registry");
    public static string RegImported => T("  已按基线恢复策略值", "  Registry values restored from baseline");
    public static string RegDeleted => T("  已删除测试创建的策略键 (基线本无)", "  Deleted test-created policy key (baseline had none)");
    public static string RegDefault => T("  UpdateDefault=1 - default, leftovers cleared", "  UpdateDefault=1 - default, leftovers cleared");
    public static string EnableOk => T("[OK] 已恢复更新。去 chrome://settings/help 点检查更新验证。",
        "[OK] Updates restored. Verify via chrome://settings/help > Check for update.");

    // ---- 出厂默认 ----
    public static string StepSvcAuto => T("[2/4] 服务回 Auto", "[2/4] Services back to Auto");
    public static string StepTaskOn => T("[3/4] 任务回启用", "[3/4] Tasks back to enabled");
    public static string StepRegDel => T("[4/4] 删除策略键", "[4/4] Delete policy key");
    public static string ToAuto(string s) => T($"  {s} -> Auto", $"  {s} -> Auto");
    public static string ToEnabled(string s) => T($"  {s} -> Enabled", $"  {s} -> Enabled");
    public static string RegKeyDeleted(string p) => T($"  已删除 {p}", $"  Deleted {p}");
    public static string ResetOk => T("[OK] 已回到出厂默认 (Auto/Ready/无策略)。", "[OK] Back to factory defaults (Auto/Ready/no policy).");

    // ---- 异常/CLI ----
    public static string ErrNeedAdmin => T("[ERROR] 需要管理员身份运行。", "[ERROR] Administrator privileges required.");
    public static string ErrCancelled => T("[ERROR] 已取消提权。", "[ERROR] Elevation cancelled.");
    public static string ErrDenied => T("[ERROR] 需要管理员身份运行(提权被拒绝)。", "[ERROR] Administrator required (elevation denied).");
    public static string ErrSvcMode(string s, string m, uint rc) =>
        T($"服务 {s} 改为 {m} 失败 (rc={rc})", $"Failed to set service {s} to {m} (rc={rc})");
    public static string ErrProc(string e, string a, int c, string err) =>
        T($"{e} {a} 失败 exit={c}: {err}", $"{e} {a} failed exit={c}: {err}");
    public static string ErrBaseBroken(string d) => T("基线文件损坏: " + d, "Baseline file broken: " + d);

    // ---- 日志/诊断 ----
    public static string VerApp => T("程序版本: ", "App version: ");
    public static string VerBuild => T("打包时间: ", "Build time: ");
    public static string VerOS => T("系统: ", "OS: ");
    public static string VerChrome => T("Chrome: ", "Chrome: ");
    public static string VerRt => T("运行时: ", "Runtime: ");
    public static string DiagExported(string z) => T($"诊断包已导出: {z}", $"Diagnostics exported: {z}");
    public static string NoBaselineFile => T("(无基线)", "(no baseline)");
}
