namespace ChromeUpdateToggle;

public partial class MainForm : Form
{
    private RadioButton rbDisable = null!;
    private RadioButton rbEnable = null!;
    private Button btnOK = null!;
    private Button btnRefresh = null!;
    private Button btnExport = null!;
    private Label lblStatus = null!;
    private TextBox txtState = null!;
    private TextBox txtLog = null!;

    public MainForm()
    {
        InitializeComponent();
        RefreshState();
    }

    private void InitializeComponent()
    {
        Text = $"Chrome 自动更新开关 v{Logger.Version}";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var grp = new GroupBox { Text = "请选择操作", Location = new Point(12, 8), Size = new Size(594, 60) };
        rbDisable = new RadioButton { Text = "禁止更新", Location = new Point(20, 24), Size = new Size(120, 24), Checked = true };
        rbEnable = new RadioButton { Text = "恢复更新", Location = new Point(160, 24), Size = new Size(120, 24) };
        var lblAuthor = new Label
        {
            Text = "作者：开源探长彪哥",
            Location = new Point(414, 24),
            Size = new Size(170, 24),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray,
        };
        grp.Controls.AddRange(new Control[] { rbDisable, rbEnable, lblAuthor });

        btnOK = new Button { Text = "确定", Location = new Point(12, 76), Size = new Size(140, 36) };
        btnRefresh = new Button { Text = "刷新状态", Location = new Point(160, 76), Size = new Size(140, 36) };
        btnExport = new Button { Text = "导出诊断", Location = new Point(486, 76), Size = new Size(120, 36) };
        lblStatus = new Label
        {
            Location = new Point(320, 76), Size = new Size(158, 36),
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        txtState = new TextBox
        {
            Location = new Point(12, 120), Size = new Size(594, 130),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        };
        txtLog = new TextBox
        {
            Location = new Point(12, 258), Size = new Size(594, 246),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Font = new Font("Consolas", 9),
        };

        btnOK.Click += async (_, _) => await RunSelectedAsync();
        btnRefresh.Click += (_, _) => RefreshState();
        btnExport.Click += async (_, _) => await ExportAsync();

        Controls.AddRange(new Control[] { grp, btnOK, btnRefresh, btnExport, lblStatus, txtState, txtLog });
    }

    private void RefreshState()
    {
        try
        {
            var (status, detail) = UpdateManager.GetUpdateStatus();
            lblStatus.Text = status;
            lblStatus.ForeColor = status switch
            {
                "已禁止更新" => Color.Red,
                "更新正常" => Color.Green,
                _ => Color.Orange,
            };
            string head = UpdateManager.IsAdministrator() ? "[管理员] " : "[非管理员, 只能查看] ";
            txtState.Text = head + $"当前: {status} ({detail})\r\n" + UpdateManager.DescribeState();
        }
        catch (Exception ex)
        {
            lblStatus.Text = "未知";
            lblStatus.ForeColor = Color.Gray;
            txtState.Text = "读取状态失败: " + ex.Message;
        }
    }

    private async Task RunSelectedAsync()
    {
        string title = rbDisable.Checked ? "禁止更新" : "恢复更新";
        string arg = rbDisable.Checked ? "--disable" : "--enable";
        SetBusy(false);
        try
        {
            if (!UpdateManager.IsAdministrator())
                Log("当前非管理员, 弹 UAC 提权执行 (点\"是\"即可)...");
            Log($"===== {title} 开始 =====");
            int rc = await Task.Run(() => Program.RunElevated(arg, Log));
            Log(rc == 0 ? $"===== {title} 成功 =====" : $"===== {title} 失败/取消 (exit={rc}) =====");
        }
        catch (Exception ex)
        {
            Log("[ERROR] " + ex.Message);
        }
        RefreshState();
        SetBusy(true);
    }

    private void SetBusy(bool enabled)
    {
        rbDisable.Enabled = rbEnable.Enabled = btnOK.Enabled = btnRefresh.Enabled = btnExport.Enabled = enabled;
    }

    private async Task ExportAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "ZIP 压缩包|*.zip",
            FileName = $"ChromeUpdateToggle-诊断-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(false);
        try
        {
            await Task.Run(() => Logger.ExportDiagnostics(dlg.FileName, Logger.Tee(Log)));
        }
        catch (Exception ex)
        {
            Log("[ERROR] " + ex.Message);
        }
        SetBusy(true);
    }

    private void Log(string s)
    {
        if (InvokeRequired) { Invoke(Log, s); return; }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
        Logger.Log(s);
    }
}
