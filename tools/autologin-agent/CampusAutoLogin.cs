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

        _timer = new System.Windows.Forms.Timer();
        _timer.Interval = 5 * 60 * 1000;
        _timer.Tick += delegate { RunOnce(false); };
        _timer.Start();

        ThreadPool.QueueUserWorkItem(delegate { RunOnce(true); });
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("立即登录", null, delegate { RunOnce(true); });
        menu.Items.Add("打开配置", null, delegate { OpenPath(_runner.ConfigPath); });
        menu.Items.Add("打开日志", null, delegate { OpenPath(_runner.LogPath); });
        menu.Items.Add("打开配置目录", null, delegate { Process.Start(_runner.AppDir); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { Exit(); });
        return menu;
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

internal sealed class Config
{
    public string Username = "";
    public string Password = "";
    public string Callback = "dr1004";
    public string PreferredProfile = "";
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
