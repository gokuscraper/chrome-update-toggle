using System.Diagnostics;

namespace ChromeUpdateToggle;

public partial class MainForm : Form
{
    private GroupBox grp = null!;
    private RadioButton rbDisable = null!;
    private RadioButton rbEnable = null!;
    private ComboBox cmbLang = null!;
    private Label lblAuthor = null!;
    private LinkLabel lnkRepo = null!;
    private Button btnOK = null!;
    private Button btnRefresh = null!;
    private Button btnExport = null!;
    private Label lblStatus = null!;
    private ProgressBar barBusy = null!;
    private TextBox txtState = null!;
    private TextBox txtLog = null!;
    private bool _loadingLang;

    public MainForm()
    {
        InitializeComponent();
        ApplyLanguage();
        try
        {
            using var s = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ChromeUpdateToggle.app.ico");
            if (s != null) Icon = new Icon(s);
        }
        catch { /* 无图标不影响运行 */ }
        // 秒开: 先摆占位, 等窗口画完再异步检测
        lblStatus.Text = Strings.Checking;
        lblStatus.ForeColor = Color.Gray;
        txtState.Text = Strings.CheckingDetail;
        Shown += async (_, _) => await RefreshAsync();
    }

    private void InitializeComponent()
    {
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        grp = new GroupBox { Location = new Point(12, 8), Size = new Size(594, 76) };
        rbDisable = new RadioButton { Location = new Point(20, 22), Size = new Size(130, 24), Checked = true };
        rbEnable = new RadioButton { Location = new Point(155, 22), Size = new Size(130, 24) };
        cmbLang = new ComboBox
        {
            Location = new Point(290, 22), Size = new Size(110, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmbLang.Items.AddRange(new object[] { Strings.LangZh, Strings.LangEn });
        lblAuthor = new Label
        {
            Location = new Point(414, 22),
            Size = new Size(170, 22),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray,
        };
        lnkRepo = new LinkLabel
        {
            Location = new Point(20, 48),
            Size = new Size(564, 20),
            Text = "GitHub: " + Strings.RepoUrl,
        };
        lnkRepo.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Strings.RepoUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log("[ERROR] " + ex.Message); }
        };
        grp.Controls.AddRange(new Control[] { rbDisable, rbEnable, cmbLang, lblAuthor, lnkRepo });

        btnOK = new Button { Location = new Point(12, 92), Size = new Size(140, 36) };
        btnRefresh = new Button { Location = new Point(160, 92), Size = new Size(140, 36) };
        btnExport = new Button { Location = new Point(486, 92), Size = new Size(120, 36) };
        lblStatus = new Label
        {
            Location = new Point(320, 92), Size = new Size(158, 36),
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        txtState = new TextBox
        {
            Location = new Point(12, 136), Size = new Size(594, 130),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        };
        txtLog = new TextBox
        {
            Location = new Point(12, 274), Size = new Size(594, 230),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Font = new Font("Consolas", 9),
        };

        btnOK.Click += async (_, _) => await RunSelectedAsync();
        btnRefresh.Click += async (_, _) => await RefreshAsync();
        btnExport.Click += async (_, _) => await ExportAsync();
        cmbLang.SelectedIndexChanged += async (_, _) =>
        {
            if (_loadingLang) return;
            Strings.SetLang(cmbLang.SelectedIndex == 1 ? "en" : "zh");
            ApplyLanguage();
            await RefreshAsync();
        };

        barBusy = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 12,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Visible = false,
        };
        Controls.Add(barBusy);

        Controls.AddRange(new Control[] { grp, btnOK, btnRefresh, btnExport, lblStatus, txtState, txtLog });
    }

    private void ApplyLanguage()
    {
        _loadingLang = true;
        Text = Strings.AppTitle;
        grp.Text = Strings.GroupOp;
        rbDisable.Text = Strings.OpDisable;
        rbEnable.Text = Strings.OpEnable;
        cmbLang.SelectedIndex = Strings.Current == "en" ? 1 : 0;
        lblAuthor.Text = Strings.Author;
        btnOK.Text = Strings.BtnOK;
        btnRefresh.Text = Strings.BtnRefresh;
        btnExport.Text = Strings.BtnExport;
        _loadingLang = false;
    }

    private async Task RefreshAsync()
    {
        SetBusy(false);
        barBusy.Visible = true;
        try
        {
            var (status, _, body, isDis, isOk) = await Task.Run(() =>
            {
                var st = UpdateManager.GetUpdateStatus();
                string head = UpdateManager.IsAdministrator() ? Strings.HeadAdmin : Strings.HeadNonAdmin;
                string b = head + Strings.StateNow(st.Status, st.Detail) + "\r\n"
                    + UpdateManager.DescribeState();
                return (st.Status, st.Detail, b,
                    st.Status == Strings.StDisabled, st.Status == Strings.StNormal);
            });
            lblStatus.Text = status;
            lblStatus.ForeColor = isDis ? Color.Red : isOk ? Color.Green : Color.Orange;
            txtState.Text = body;
        }
        catch (Exception ex)
        {
            lblStatus.Text = "?";
            lblStatus.ForeColor = Color.Gray;
            txtState.Text = Strings.StateReadFail + ex.Message;
        }
        barBusy.Visible = false;
        SetBusy(true);
    }

    private async Task RunSelectedAsync()
    {
        string title = rbDisable.Checked ? Strings.OpDisable : Strings.OpEnable;
        string arg = rbDisable.Checked ? "--disable" : "--enable";
        SetBusy(false);
        try
        {
            if (!UpdateManager.IsAdministrator())
                Log(Strings.NeedAdminUI);
            Log(Strings.StartedSection(title));
            int rc = await Task.Run(() => Program.RunElevated(arg, Log));
            Log(rc == 0 ? Strings.DoneOk(title) : Strings.DoneFail(title, rc));
        }
        catch (Exception ex)
        {
            Log("[ERROR] " + ex.Message);
        }
        await RefreshAsync();
    }

    private void SetBusy(bool enabled)
    {
        rbDisable.Enabled = rbEnable.Enabled = cmbLang.Enabled
            = btnOK.Enabled = btnRefresh.Enabled = btnExport.Enabled = enabled;
    }

    private async Task ExportAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = Strings.ZipFilter,
            FileName = Strings.DiagDefaultName,
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
