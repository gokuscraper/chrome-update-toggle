using System.Diagnostics;

namespace ChromeUpdateToggle;

static class Program
{
    private const string ElevatedFlag = "CUT_ELEVATED";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(a => a is "--disable" or "1"))
            return RunAction(l => UpdateManager.Disable(l), args);
        if (args.Any(a => a is "--enable" or "2"))
            return RunAction(l => UpdateManager.Enable(l), args);
        if (args.Any(a => a is "--reset" or "3"))
            return RunAction(l => UpdateManager.ResetToDefaults(l), args);
        if (args.Any(a => a is "--lang"))
        {
            string v = args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault() ?? "zh";
            Strings.SetLang(v.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh");
            Console.WriteLine("lang=" + Strings.Current);
            return 0;
        }
        if (args.Any(a => a is "--export-diagnostics"))
        {
            // 导出诊断包(只读为主, 不提权)
            try
            {
                string zip = args.SkipWhile(a => a != "--export-diagnostics").Skip(1)
                    .FirstOrDefault()
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        Strings.DiagDefaultName);
                Logger.ExportDiagnostics(zip, Logger.Tee(Console.WriteLine));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] " + ex.Message);
                return 2;
            }
        }
        if (args.Any(a => a is "--status"))
        {
            // 只读查询, 不提权
            try
            {
                var (status, detail) = UpdateManager.GetUpdateStatus();
                Console.WriteLine($"{status} ({detail})");
                Console.WriteLine(UpdateManager.DescribeState());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] " + ex.Message);
                return 2;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>写操作: 非管理员时自动拉起提权副本执行, 用户只需点一次 UAC。</summary>
    private static int RunAction(Action<Action<string>> action, string[] args)
    {
        if (UpdateManager.IsAdministrator())
        {
            var tee = Logger.Tee(Console.WriteLine);
            try
            {
                action(tee);
                return 0;
            }
            catch (Exception ex)
            {
                tee("[ERROR] " + ex.Message);
                Logger.Log(ex.ToString());
                return 2;
            }
        }

        if (Environment.GetEnvironmentVariable(ElevatedFlag) == "1")
        {
            Console.Error.WriteLine(Strings.ErrDenied);
            return 1;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
                UseShellExecute = true,
                Verb = "runas",
            };
            psi.Environment[ElevatedFlag] = "1";
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Console.Error.WriteLine(Strings.ErrCancelled);
            return 1;
        }
    }

    /// <summary>供 UI 调用: 非管理员时提权执行指定动作, 回传退出码。</summary>
    public static int RunElevated(string actionArg, Action<string>? log = null)
    {
        if (UpdateManager.IsAdministrator())
        {
            // UI 传的 Log 自带落盘, CLI 才包 Tee
            var write = log ?? Logger.Tee(Console.WriteLine);
            try
            {
                if (actionArg == "--disable") UpdateManager.Disable(write);
                else UpdateManager.Enable(write);
                return 0;
            }
            catch (Exception ex)
            {
                write("[ERROR] " + ex.Message);
                Logger.Log(ex.ToString());
                return 2;
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"\"{actionArg}\"",
                UseShellExecute = true,
                Verb = "runas",
            };
            psi.Environment[ElevatedFlag] = "1";
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return 1;
        }
    }
}
