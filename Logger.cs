using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace ChromeUpdateToggle;

/// <summary>常驻文件日志 + 版本信息 + 诊断包导出。</summary>
public static class Logger
{
    public const string Version = "1.3.0";

    private static readonly object Gate = new();
    private static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>写一行带时间戳日志(UI/CLI共用, 按天切分)。</summary>
    public static void Log(string s)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(
                    Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                    $"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
            }
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    /// <summary>把 UI/控制台输出和文件日志合二为一。</summary>
    public static Action<string> Tee(Action<string> ui)
    {
        return s => { try { ui(s); } catch { } Log(s); };
    }

    public static string BuildInfo()
    {
        string exe = Environment.ProcessPath ?? AppContext.BaseDirectory;
        string buildTime;
        try { buildTime = File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm"); }
        catch { buildTime = "?"; }
        string chromeVer;
        try
        {
            chromeVer = FileVersionInfo.GetVersionInfo(
                @"C:\Program Files\Google\Chrome\Application\chrome.exe").FileVersion ?? "?";
        }
        catch { chromeVer = "?"; }
        return $"{Strings.VerApp}v{Version}\r\n{Strings.VerBuild}{buildTime}\r\n" +
               $"{Strings.VerOS}{Environment.OSVersion}\r\n{Strings.VerChrome}{chromeVer}\r\n" +
               $"{Strings.VerRt}{Environment.Version}\r\n";
    }

    /// <summary>导出诊断包: 状态 + 最新基线 + 近3天日志 + 版本, 打成zip。</summary>
    public static string ExportDiagnostics(string zipPath, Action<string> log)
    {
        string tmp = Path.Combine(Path.GetTempPath(),
            "CUT-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(tmp);

        var (status, detail) = UpdateManager.GetUpdateStatus();
        bool en = Strings.Current == "en";
        string stateFile = en ? "status.txt" : "状态.txt";
        string verFile = en ? "version.txt" : "版本.txt";
        File.WriteAllText(Path.Combine(tmp, stateFile),
            $"{Strings.StateNow(status, detail)}\r\n\r\n{UpdateManager.DescribeState()}");

        string? newest = UpdateManager.NewestBaselineDir();
        if (newest != null && File.Exists(Path.Combine(newest, "baseline.json")))
            File.Copy(Path.Combine(newest, "baseline.json"),
                Path.Combine(tmp, "baseline.json"));
        else
            File.WriteAllText(Path.Combine(tmp, "baseline.json"), Strings.NoBaselineFile);

        string logDst = Path.Combine(tmp, "logs");
        Directory.CreateDirectory(logDst);
        if (Directory.Exists(LogDir))
        {
            foreach (var f in Directory.GetFiles(LogDir, "*.log")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(3))
                File.Copy(f, Path.Combine(logDst, Path.GetFileName(f)));
        }

        File.WriteAllText(Path.Combine(tmp, verFile), BuildInfo());

        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(tmp, zipPath);
        Directory.Delete(tmp, true);
        log(Strings.DiagExported(zipPath));
        return zipPath;
    }
}
