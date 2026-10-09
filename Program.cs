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
            try
            {
                action(Console.WriteLine);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] " + ex.Message);
                try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "last-error.log"), ex.ToString()); }
                catch { }
                return 2;
            }
        }

        if (Environment.GetEnvironmentVariable(ElevatedFlag) == "1")
        {
            Console.Error.WriteLine("[ERROR] 需要管理员身份运行(提权被拒绝)。");
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
            Console.Error.WriteLine("[ERROR] 已取消提权。");
            return 1;
        }
    }

    /// <summary>供 UI 调用: 非管理员时提权执行指定动作, 回传退出码。</summary>
    public static int RunElevated(string actionArg, Action<string>? log = null)
    {
        if (UpdateManager.IsAdministrator())
        {
            try
            {
                var write = log ?? Console.WriteLine;
                if (actionArg == "--disable") UpdateManager.Disable(write);
                else UpdateManager.Enable(write);
                return 0;
            }
            catch (Exception ex)
            {
                (log ?? Console.Error.WriteLine)("[ERROR] " + ex.Message);
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
