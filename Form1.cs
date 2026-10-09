namespace ChromeUpdateToggle;

public partial class MainForm : Form
{
    private GroupBox grp = null!;
    private RadioButton rbDisable = null!;
    private RadioButton rbEnable = null!;
    private ComboBox cmbLang = null!;
    private Label lblAuthor = null!;
    private Button btnOK = null!;
    private Button btnRefresh = null!;
    private Button btnExport = null!;
    private Label lblStatus = null!;
    private TextBox txtState = null!;
    private TextBox txtLog = null!;
    private bool _loadingLang;

    public MainForm()
    {
        InitializeComponent();
        ApplyLanguage();
        RefreshState();
    }

    private void InitializeComponent()
    {
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        grp = new GroupBox { Location = new Point(12, 8), Size = new Size(594, 60) };
        rbDisable = new RadioButton { Location = new Point(20, 24), Size = new Size(130, 24), Checked = true };
        rbEnable = new RadioButton { Location = new Point(155, 24), Size = new Size(130, 24) };
        cmbLang = new ComboBox
        {
            Location = new Point(290, 24), Size = new Size(110, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmbLang.Items.AddRange(new object[] { Strings.LangZh, Strings.LangEn });
        lblAuthor = new Label
        {
            Location = new Point(414, 24),
            Size = new Size(170, 24),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray,
        };
        grp.Controls.AddRange(new Control[] { rbDisable, rbEnable, cmbLang, lblAuthor });

        btnOK = new Button { Location = new Point(12, 76), Size = new Size(140, 36) };
        btnRefresh = new Button { Location = new Point(160, 76), Size = new Size(140, 36) };
        btnExport = new Button { Location = new Point(486, 76), Size = new Size(120, 36) };
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
        cmbLang.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingLang) return;
            Strings.SetLang(cmbLang.SelectedIndex == 1 ? "en" : "zh");
            ApplyLanguage();
            RefreshState();
        };

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

    private void RefreshState()
    {
        try
        {
            var (status, detail) = UpdateManager.GetUpdateStatus();
            lblStatus.Text = status;
            lblStatus.ForeColor = status == Strings.StDisabled ? Color.Red
                : status == Strings.StNormal ? Color.Green : Color.Orange;
            string head = UpdateManager.IsAdministrator() ? Strings.HeadAdmin : Strings.HeadNonAdmin;
            txtState.Text = head + Strings.StateNow(status, detail) + "\r\n" + UpdateManager.DescribeState();
        }
        catch (Exception ex)
        {
            lblStatus.Text = "?";
            lblStatus.ForeColor = Color.Gray;
            txtState.Text = Strings.StateReadFail + ex.Message;
        }
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
        RefreshState();
        SetBusy(true);
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
