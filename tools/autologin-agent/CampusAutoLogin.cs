using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool created;
        using (new Mutex(true, "CampusAutoLogin.LegacyAgent.Singleton", out created))
        {
            if (!created)
            {
                MessageBox.Show("校园网自动登录已经在后台运行。", "Campus AutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AgentContext());
        }
    }
}

internal sealed class AgentContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AuthRunner _runner;
    private MainForm _mainForm;
    private bool _running;

    public AgentContext()
    {
        _runner = new AuthRunner();
        _runner.EnsureConfig();

        _icon = new NotifyIcon();
        _icon.Icon = SystemIcons.Application;
        _icon.Text = "校园网自动登录";
        _icon.Visible = true;
        _icon.ContextMenuStrip = BuildMenu();
        _icon.DoubleClick += delegate { ShowMainForm(); };

        _timer = new System.Windows.Forms.Timer();
        _timer.Tick += delegate { RunOnce(false); };
        ApplyTimerInterval();
        _timer.Start();

        ThreadPool.QueueUserWorkItem(delegate { RunOnce(true); });
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面", null, delegate { ShowMainForm(); });
        menu.Items.Add("立即登录", null, delegate { RunOnce(true); });
        menu.Items.Add("编辑原始配置", null, delegate { OpenPath(_runner.ConfigPath); });
        menu.Items.Add("打开日志", null, delegate { OpenPath(_runner.LogPath); });
        menu.Items.Add("打开配置目录", null, delegate { Process.Start(_runner.AppDir); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { Exit(); });
        return menu;
    }

    private void ShowMainForm()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(_runner, delegate { RunOnce(true); }, ApplyTimerInterval);
        }
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void ApplyTimerInterval()
    {
        int minutes = 5;
        try
        {
            minutes = Config.Load(_runner.ConfigPath).IntervalMinutes;
        }
        catch
        {
            minutes = 5;
        }
        if (minutes < 1) minutes = 1;
        if (minutes > 1440) minutes = 1440;
        _timer.Interval = minutes * 60 * 1000;
    }

    private void RunOnce(bool showBalloon)
    {
        if (_running) return;
        _running = true;
        try
        {
            var result = _runner.Run();
            _icon.Text = result.Success ? "校园网自动登录：在线" : "校园网自动登录：需要检查";
            if (showBalloon || !result.Success)
            {
                _icon.ShowBalloonTip(3000, "校园网自动登录", result.Message, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _runner.WriteLog("Unhandled: " + ex.Message);
            _icon.ShowBalloonTip(3000, "校园网自动登录", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _running = false;
        }
    }

    private static void OpenPath(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "");
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Exit()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        ExitThread();
    }
}

internal sealed class AuthRunner
{
    public readonly string AppDir;
    public readonly string ConfigPath;
    public readonly string LogPath;

    public AuthRunner()
    {
        AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CampusAutoLogin");
        ConfigPath = Path.Combine(AppDir, "config.ini");
        LogPath = Path.Combine(AppDir, "autologin.log");
        Directory.CreateDirectory(AppDir);
    }

    public void EnsureConfig()
    {
        if (File.Exists(ConfigPath)) return;
        File.WriteAllText(ConfigPath,
@"username=YOUR_STUDENT_ID
password=YOUR_PASSWORD
callback=dr1004
preferred_profile=
interval_minutes=5

[profile:sushe]
portal_ip=172.17.253.3
match_prefixes=10.12.,10.31.,10.33.
maintenance_enabled=false

[profile:lab-p]
portal_ip=192.168.199.21
match_prefixes=10.8.
maintenance_enabled=true
maintenance_start=04:00
maintenance_end=04:15
");
    }

    public Result Run()
    {
        var cfg = Config.Load(ConfigPath);
        if (String.IsNullOrWhiteSpace(cfg.Username) || String.IsNullOrWhiteSpace(cfg.Password) ||
            cfg.Username == "YOUR_STUDENT_ID" || cfg.Password == "YOUR_PASSWORD")
        {
            WriteLog("Config incomplete");
            return Result.Fail("请先右键托盘图标，打开配置并填写账号密码。");
        }
        if (cfg.Profiles.Count == 0) return Result.Fail("配置中没有校园网入口。");

        string localIp = GetPreferredLocalIPv4();
        Profile profile = SelectProfile(cfg, localIp);
        if (profile.MaintenanceEnabled && InMaintenanceWindow(profile.MaintenanceStart, profile.MaintenanceEnd))
        {
            string msg = "维护窗口，暂不登录：" + profile.Name;
            WriteLog(msg);
            return Result.Ok(msg);
        }

        Status status = TestStatus(profile.PortalIp);
        if (status != null && !String.IsNullOrWhiteSpace(status.Ss5)) localIp = status.Ss5;

        string url = BuildLoginUrl(cfg, profile, localIp);
        string raw = HttpGet(url, 10000);
        string payload = ParseJsonp(raw);
        int result = ParseInt(payload, "result");
        int retCode = ParseInt(payload, "ret_code");
        string msgText = ParseString(payload, "msg");

        if (result == 1)
        {
            string msg = "登录成功：" + profile.Name;
            WriteLog(msg);
            return Result.Ok(msg);
        }
        if (retCode == 2)
        {
            string msg = "已在线：" + profile.Name;
            WriteLog(msg);
            return Result.Ok(msg);
        }

        string fail = "登录失败：" + profile.Name + " result=" + result + " ret_code=" + retCode + " msg=" + msgText;
        WriteLog(fail);
        return Result.Fail(fail);
    }

    public void WriteLog(string message)
    {
        File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine);
    }

    private static Profile SelectProfile(Config cfg, string localIp)
    {
        if (!String.IsNullOrWhiteSpace(cfg.PreferredProfile))
        {
            foreach (Profile p in cfg.Profiles)
                if (String.Equals(p.Name, cfg.PreferredProfile, StringComparison.OrdinalIgnoreCase)) return p;
        }

        foreach (Profile p in cfg.Profiles)
            foreach (string prefix in p.MatchPrefixes)
                if (!String.IsNullOrWhiteSpace(prefix) && localIp.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return p;

        foreach (Profile p in cfg.Profiles)
            if (TestStatus(p.PortalIp) != null) return p;

        return cfg.Profiles[0];
    }

    private static Status TestStatus(string portalIp)
    {
        try
        {
            string raw = HttpGet("http://" + portalIp + "/drcom/chkstatus?callback=dr1002", 6000);
            string payload = ParseJsonp(raw);
            return new Status { Ss5 = ParseString(payload, "ss5") };
        }
        catch
        {
            return null;
        }
    }

    private static string BuildLoginUrl(Config cfg, Profile p, string localIp)
    {
        return "http://" + p.PortalIp + ":801/eportal/?c=Portal&a=login&callback=" + Uri.EscapeDataString(cfg.Callback) +
               "&login_method=1&user_account=%2C0%2C" + Uri.EscapeDataString(cfg.Username) +
               "&user_password=" + Uri.EscapeDataString(cfg.Password) +
               "&wlan_user_ip=" + Uri.EscapeDataString(localIp) +
               "&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=3.3.3&v=1954";
    }

    private static string HttpGet(string url, int timeoutMs)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Timeout = timeoutMs;
        req.ReadWriteTimeout = timeoutMs;
        req.UserAgent = "CampusAutoLogin/1.0";
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var stream = resp.GetResponseStream())
        using (var reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }

    private static string ParseJsonp(string text)
    {
        string trimmed = text.Trim();
        int start = trimmed.IndexOf('(');
        int end = trimmed.LastIndexOf(')');
        return start >= 0 && end > start ? trimmed.Substring(start + 1, end - start - 1) : trimmed;
    }

    private static int ParseInt(string json, string name)
    {
        Match m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)");
        return m.Success ? Int32.Parse(m.Groups[1].Value) : 0;
    }

    private static string ParseString(string json, string name)
    {
        Match m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
    }

    private static bool InMaintenanceWindow(string startText, string endText)
    {
        TimeSpan start, end;
        if (!TimeSpan.TryParse(startText, out start)) start = new TimeSpan(4, 0, 0);
        if (!TimeSpan.TryParse(endText, out end)) end = new TimeSpan(4, 15, 0);
        TimeSpan now = DateTime.Now.TimeOfDay;
        return start <= end ? now >= start && now < end : now >= start || now < end;
    }

    private static string GetPreferredLocalIPv4()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            string name = nic.Name + " " + nic.Description;
            if (ContainsAny(name, "Virtual", "Hyper-V", "Tunnel", "Loopback", "Meta")) continue;
            foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
            {
                string ip = addr.Address.ToString();
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork && IsUsableIPv4(ip)) return ip;
            }
        }
        return "";
    }

    private static bool ContainsAny(string text, params string[] words)
    {
        foreach (string word in words)
            if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static bool IsUsableIPv4(string ip)
    {
        return !ip.StartsWith("127.") && !ip.StartsWith("169.254.") && !ip.StartsWith("198.18.") && !ip.StartsWith("192.168.50.");
    }
}

internal sealed class MainForm : Form
{
    private readonly AuthRunner _runner;
    private readonly Action _loginNow;
    private readonly TextBox _username = new TextBox();
    private readonly TextBox _password = new TextBox();
    private readonly ComboBox _preferred = new ComboBox();
    private readonly NumericUpDown _intervalMinutes = new NumericUpDown();
    private readonly CheckBox _maintenance = new CheckBox();
    private readonly TextBox _maintenanceStart = new TextBox();
    private readonly TextBox _maintenanceEnd = new TextBox();
    private readonly TextBox _status = new TextBox();
    private readonly TextBox _log = new TextBox();
    private readonly Label _configPath = new Label();

    private readonly Action _applyTimerInterval;

    public MainForm(AuthRunner runner, Action loginNow, Action applyTimerInterval)
    {
        _runner = runner;
        _loginNow = loginNow;
        _applyTimerInterval = applyTimerInterval;

        Text = "校园网自动登录";
        Width = 720;
        Height = 500;
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildHomePage());
        tabs.TabPages.Add(BuildSettingsPage());
        Controls.Add(tabs);

        Load += delegate
        {
            LoadConfigToUi();
            RefreshLog();
        };
    }

    private TabPage BuildHomePage()
    {
        var page = new TabPage("主页");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _status.Multiline = true;
        _status.ReadOnly = true;
        _status.BorderStyle = BorderStyle.FixedSingle;
        _status.Dock = DockStyle.Fill;
        _status.ScrollBars = ScrollBars.Vertical;
        _status.Text = "后台已启动。配置账号后可点击“立即登录”。";
        root.SetColumnSpan(_status, 2);
        root.Controls.Add(_status, 0, 0);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.WordWrap = false;
        root.SetColumnSpan(_log, 2);
        root.Controls.Add(_log, 0, 1);

        var loginButton = new Button { Text = "立即登录", Width = 120, Height = 34, Anchor = AnchorStyles.Left };
        loginButton.Click += delegate
        {
            _loginNow();
            RefreshLog();
            _status.Text = "已触发登录。结果请查看日志。";
        };
        var refreshButton = new Button { Text = "刷新日志", Width = 120, Height = 34, Anchor = AnchorStyles.Left };
        refreshButton.Click += delegate { RefreshLog(); };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(loginButton);
        buttons.Controls.Add(refreshButton);
        root.SetColumnSpan(buttons, 2);
        root.Controls.Add(buttons, 0, 2);

        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildSettingsPage()
    {
        var page = new TabPage("设置");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(28) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabel(root, "学号", 0);
        _username.Dock = DockStyle.Fill;
        root.Controls.Add(_username, 1, 0);
        AddLabel(root, "密码", 1);
        _password.PasswordChar = '*';
        _password.Dock = DockStyle.Fill;
        root.Controls.Add(_password, 1, 1);
        AddLabel(root, "优先场景", 2);
        _preferred.DropDownStyle = ComboBoxStyle.DropDownList;
        _preferred.Dock = DockStyle.Fill;
        root.Controls.Add(_preferred, 1, 2);
        AddLabel(root, "重试间隔", 3);
        var intervalPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _intervalMinutes.Minimum = 1;
        _intervalMinutes.Maximum = 1440;
        _intervalMinutes.Width = 90;
        intervalPanel.Controls.Add(_intervalMinutes);
        intervalPanel.Controls.Add(new Label { Text = "分钟", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 7, 0, 0) });
        root.Controls.Add(intervalPanel, 1, 3);

        AddLabel(root, "维护窗口", 4);
        _maintenance.Text = "实验室 04:00-04:15 暂停重连";
        _maintenance.Dock = DockStyle.Fill;
        root.Controls.Add(_maintenance, 1, 4);
        AddLabel(root, "开始时间", 5);
        _maintenanceStart.Dock = DockStyle.Fill;
        root.Controls.Add(_maintenanceStart, 1, 5);
        AddLabel(root, "结束时间", 6);
        _maintenanceEnd.Dock = DockStyle.Fill;
        root.Controls.Add(_maintenanceEnd, 1, 6);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = "保存", Width = 100, Height = 32 };
        save.Click += delegate { SaveUiToConfig(); };
        var openRaw = new Button { Text = "高级配置", Width = 100, Height = 32 };
        openRaw.Click += delegate { Process.Start(new ProcessStartInfo(_runner.ConfigPath) { UseShellExecute = true }); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(openRaw);
        root.SetColumnSpan(buttons, 2);
        root.Controls.Add(buttons, 0, 7);

        _configPath.AutoSize = false;
        _configPath.Dock = DockStyle.Fill;
        _configPath.TextAlign = ContentAlignment.BottomLeft;
        root.SetColumnSpan(_configPath, 2);
        root.Controls.Add(_configPath, 0, 8);

        page.Controls.Add(root);
        return page;
    }

    private static void AddLabel(TableLayoutPanel root, string text, int row)
    {
        root.Controls.Add(new Label { Text = text + ":", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
    }

    private void LoadConfigToUi()
    {
        var cfg = Config.Load(_runner.ConfigPath);
        _username.Text = cfg.Username;
        _password.Text = cfg.Password;
        _preferred.Items.Clear();
        _preferred.Items.Add("");
        foreach (Profile p in cfg.Profiles) _preferred.Items.Add(p.Name);
        _preferred.SelectedItem = cfg.PreferredProfile;
        if (_preferred.SelectedIndex < 0) _preferred.SelectedIndex = 0;
        _intervalMinutes.Value = Math.Min(_intervalMinutes.Maximum, Math.Max(_intervalMinutes.Minimum, cfg.IntervalMinutes));

        Profile lab = FindProfile(cfg, "lab-p");
        if (lab != null)
        {
            _maintenance.Checked = lab.MaintenanceEnabled;
            _maintenanceStart.Text = lab.MaintenanceStart;
            _maintenanceEnd.Text = lab.MaintenanceEnd;
        }
        _configPath.Text = "配置文件：" + _runner.ConfigPath;
    }

    private void SaveUiToConfig()
    {
        var cfg = Config.Load(_runner.ConfigPath);
        cfg.Username = _username.Text.Trim();
        cfg.Password = _password.Text;
        cfg.PreferredProfile = Convert.ToString(_preferred.SelectedItem) ?? "";
        cfg.IntervalMinutes = Convert.ToInt32(_intervalMinutes.Value);
        Profile lab = FindProfile(cfg, "lab-p");
        if (lab != null)
        {
            lab.MaintenanceEnabled = _maintenance.Checked;
            lab.MaintenanceStart = String.IsNullOrWhiteSpace(_maintenanceStart.Text) ? "04:00" : _maintenanceStart.Text.Trim();
            lab.MaintenanceEnd = String.IsNullOrWhiteSpace(_maintenanceEnd.Text) ? "04:15" : _maintenanceEnd.Text.Trim();
        }
        cfg.Save(_runner.ConfigPath);
        _applyTimerInterval();
        MessageBox.Show("配置已保存。", "校园网自动登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RefreshLog()
    {
        if (!File.Exists(_runner.LogPath))
        {
            _log.Text = "";
            return;
        }
        string[] lines = File.ReadAllLines(_runner.LogPath);
        int start = Math.Max(0, lines.Length - 80);
        var tail = new List<string>();
        for (int i = start; i < lines.Length; i++) tail.Add(lines[i]);
        _log.Text = String.Join(Environment.NewLine, tail.ToArray());
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static Profile FindProfile(Config cfg, string name)
    {
        foreach (Profile p in cfg.Profiles)
            if (String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}

internal sealed class Config
{
    public string Username = "";
    public string Password = "";
    public string Callback = "dr1004";
    public string PreferredProfile = "";
    public int IntervalMinutes = 5;
    public readonly List<Profile> Profiles = new List<Profile>();

    public static Config Load(string path)
    {
        var cfg = new Config();
        Profile current = null;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[profile:") && line.EndsWith("]"))
            {
                current = new Profile { Name = line.Substring(9, line.Length - 10) };
                cfg.Profiles.Add(current);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string value = line.Substring(eq + 1).Trim();
            if (current == null)
            {
                if (key == "username") cfg.Username = value;
                else if (key == "password") cfg.Password = value;
                else if (key == "callback") cfg.Callback = value;
                else if (key == "preferred_profile") cfg.PreferredProfile = value;
                else if (key == "interval_minutes")
                {
                    int minutes;
                    if (Int32.TryParse(value, out minutes)) cfg.IntervalMinutes = minutes;
                }
            }
            else
            {
                if (key == "portal_ip") current.PortalIp = value;
                else if (key == "match_prefixes") current.MatchPrefixes.AddRange(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (key == "maintenance_enabled") current.MaintenanceEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (key == "maintenance_start") current.MaintenanceStart = value;
                else if (key == "maintenance_end") current.MaintenanceEnd = value;
            }
        }
        return cfg;
    }

    public void Save(string path)
    {
        var lines = new List<string>();
        lines.Add("username=" + Username);
        lines.Add("password=" + Password);
        lines.Add("callback=" + Callback);
        lines.Add("preferred_profile=" + PreferredProfile);
        lines.Add("interval_minutes=" + IntervalMinutes);
        lines.Add("");

        foreach (Profile p in Profiles)
        {
            lines.Add("[profile:" + p.Name + "]");
            lines.Add("portal_ip=" + p.PortalIp);
            lines.Add("match_prefixes=" + String.Join(",", p.MatchPrefixes.ToArray()));
            lines.Add("maintenance_enabled=" + (p.MaintenanceEnabled ? "true" : "false"));
            if (!String.IsNullOrWhiteSpace(p.MaintenanceStart)) lines.Add("maintenance_start=" + p.MaintenanceStart);
            if (!String.IsNullOrWhiteSpace(p.MaintenanceEnd)) lines.Add("maintenance_end=" + p.MaintenanceEnd);
            lines.Add("");
        }

        File.WriteAllLines(path, lines.ToArray());
    }
}

internal sealed class Profile
{
    public string Name = "";
    public string PortalIp = "";
    public readonly List<string> MatchPrefixes = new List<string>();
    public bool MaintenanceEnabled;
    public string MaintenanceStart = "04:00";
    public string MaintenanceEnd = "04:15";
}

internal sealed class Status
{
    public string Ss5 = "";
}

internal sealed class Result
{
    public bool Success;
    public string Message;

    private Result(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public static Result Ok(string message) { return new Result(true, message); }
    public static Result Fail(string message) { return new Result(false, message); }
}
