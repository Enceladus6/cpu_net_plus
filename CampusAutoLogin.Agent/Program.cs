using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampusAutoLogin.Agent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "CampusAutoLogin.Agent.Singleton", out var created);
        if (!created)
        {
            MessageBox.Show("校园网自动登录已经在后台运行。", "Campus AutoLogin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new AgentApplicationContext());
    }
}

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AuthRunner _runner;
    private bool _isRunning;

    public AgentApplicationContext()
    {
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CampusAutoLogin");
        Directory.CreateDirectory(appDir);

        _runner = new AuthRunner(appDir);
        _runner.EnsureConfig();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "校园网自动登录",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _timer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
        _timer.Tick += async (_, _) => await RunOnceAsync(false);
        _timer.Start();

        _ = RunOnceAsync(true);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("立即登录", null, async (_, _) => await RunOnceAsync(true));
        menu.Items.Add("打开配置", null, (_, _) => OpenFile(_runner.ConfigPath));
        menu.Items.Add("打开日志", null, (_, _) => OpenFile(_runner.LogPath));
        menu.Items.Add("打开配置目录", null, (_, _) => Process.Start(new ProcessStartInfo(_runner.AppDir) { UseShellExecute = true }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Exit());
        return menu;
    }

    private async Task RunOnceAsync(bool showBalloon)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        try
        {
            var result = await _runner.RunAsync();
            _notifyIcon.Text = result.IsSuccess ? "校园网自动登录：在线" : "校园网自动登录：需要检查";
            if (showBalloon || !result.IsSuccess)
            {
                _notifyIcon.ShowBalloonTip(3000, "校园网自动登录", result.Message, result.IsSuccess ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _runner.WriteLog("Unhandled: " + ex.Message);
            _notifyIcon.ShowBalloonTip(3000, "校园网自动登录", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private static Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "shinnku.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Exit()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }
}

internal sealed class AuthRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public AuthRunner(string appDir)
    {
        AppDir = appDir;
        ConfigPath = Path.Combine(appDir, "config.json");
        LogPath = Path.Combine(appDir, "autologin.log");
    }

    public string AppDir { get; }
    public string ConfigPath { get; }
    public string LogPath { get; }

    public void EnsureConfig()
    {
        if (File.Exists(ConfigPath))
        {
            return;
        }

        var config = new LoginConfig
        {
            Username = "YOUR_STUDENT_ID",
            Password = "YOUR_PASSWORD",
            Callback = "dr1004",
            PreferredProfile = "",
            Profiles =
            [
                new LoginProfile
                {
                    Name = "sushe",
                    PortalIp = "172.17.253.3",
                    MatchPrefixes = ["10.12.", "10.31.", "10.33."],
                    MaintenanceEnabled = false
                },
                new LoginProfile
                {
                    Name = "lab-p",
                    PortalIp = "192.168.199.21",
                    MatchPrefixes = ["10.8."],
                    MaintenanceEnabled = true,
                    MaintenanceStart = "04:00",
                    MaintenanceEnd = "04:15"
                }
            ]
        };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    public async Task<AuthResult> RunAsync()
    {
        var config = JsonSerializer.Deserialize<LoginConfig>(await File.ReadAllTextAsync(ConfigPath), JsonOptions)
            ?? throw new InvalidOperationException("配置文件无法读取。");

        if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password) ||
            config.Username == "YOUR_STUDENT_ID" || config.Password == "YOUR_PASSWORD")
        {
            WriteLog("Config incomplete");
            return AuthResult.Fail("请先右键托盘图标，打开配置并填写账号密码。");
        }

        if (config.Profiles.Count == 0)
        {
            return AuthResult.Fail("配置中没有校园网入口。");
        }

        var localIp = GetPreferredLocalIPv4();
        var profile = await SelectProfileAsync(config, localIp);
        if (profile is null)
        {
            return AuthResult.Fail("没有可用的登录配置。");
        }

        if (profile.MaintenanceEnabled && InMaintenanceWindow(profile.MaintenanceStart, profile.MaintenanceEnd))
        {
            var msg = $"维护窗口 {profile.MaintenanceStart}-{profile.MaintenanceEnd}，暂不登录。";
            WriteLog(msg);
            return AuthResult.Ok(msg);
        }

        var status = await TestStatusEndpointAsync(profile.PortalIp);
        if (!string.IsNullOrWhiteSpace(status?.Ss5))
        {
            localIp = status.Ss5;
        }

        var url = BuildLoginUrl(config, profile, localIp);
        var raw = await _http.GetStringAsync(url);
        var payload = ParseJsonpPayload(raw);
        var response = JsonSerializer.Deserialize<LoginResponse>(payload, JsonOptions);
        if (response is null)
        {
            WriteLog("Empty login response");
            return AuthResult.Fail("认证返回为空。");
        }

        if (response.Result == 1)
        {
            var msg = $"登录成功：{profile.Name}";
            WriteLog(msg);
            return AuthResult.Ok(msg);
        }

        if (response.RetCode == 2)
        {
            var msg = $"已在线：{profile.Name}";
            WriteLog(msg);
            return AuthResult.Ok(msg);
        }

        var fail = $"登录失败：{profile.Name} result={response.Result} ret_code={response.RetCode} msg={response.Msg}";
        WriteLog(fail);
        return AuthResult.Fail(fail);
    }

    public void WriteLog(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
        File.AppendAllText(LogPath, line);
    }

    private async Task<LoginProfile?> SelectProfileAsync(LoginConfig config, string localIp)
    {
        if (!string.IsNullOrWhiteSpace(config.PreferredProfile))
        {
            var preferred = config.Profiles.FirstOrDefault(x => string.Equals(x.Name, config.PreferredProfile, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        foreach (var profile in config.Profiles)
        {
            if (profile.MatchPrefixes.Any(prefix => localIp.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return profile;
            }
        }

        foreach (var profile in config.Profiles)
        {
            var status = await TestStatusEndpointAsync(profile.PortalIp);
            if (!string.IsNullOrWhiteSpace(status?.Ss5))
            {
                return profile;
            }
        }

        return config.Profiles[0];
    }

    private async Task<StatusResponse?> TestStatusEndpointAsync(string portalIp)
    {
        try
        {
            var raw = await _http.GetStringAsync($"http://{portalIp}/drcom/chkstatus?callback=dr1002");
            var payload = ParseJsonpPayload(raw);
            return JsonSerializer.Deserialize<StatusResponse>(payload, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildLoginUrl(LoginConfig config, LoginProfile profile, string localIp)
    {
        var callback = string.IsNullOrWhiteSpace(config.Callback) ? "dr1004" : config.Callback;
        return $"http://{profile.PortalIp}:801/eportal/?c=Portal&a=login&callback={callback}&login_method=1" +
               $"&user_account=%2C0%2C{Uri.EscapeDataString(config.Username)}" +
               $"&user_password={Uri.EscapeDataString(config.Password)}" +
               $"&wlan_user_ip={Uri.EscapeDataString(localIp)}&wlan_user_ipv6=&wlan_user_mac=000000000000" +
               $"&wlan_ac_ip=&wlan_ac_name=&jsVersion=3.3.3&v=1954";
    }

    private static string ParseJsonpPayload(string text)
    {
        var trimmed = text.Trim();
        var start = trimmed.IndexOf('(');
        var end = trimmed.LastIndexOf(')');
        return start >= 0 && end > start ? trimmed[(start + 1)..end] : trimmed;
    }

    private static bool InMaintenanceWindow(string startText, string endText)
    {
        if (!TimeSpan.TryParse(startText, out var start))
        {
            start = new TimeSpan(4, 0, 0);
        }
        if (!TimeSpan.TryParse(endText, out var end))
        {
            end = new TimeSpan(4, 15, 0);
        }

        var now = DateTime.Now.TimeOfDay;
        return start <= end ? now >= start && now < end : now >= start || now < end;
    }

    private static string GetPreferredLocalIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            var name = nic.Name + " " + nic.Description;
            if (name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ip = nic.GetIPProperties().UnicastAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address.ToString())
                .FirstOrDefault(IsUsableIPv4);
            if (ip is not null)
            {
                return ip;
            }
        }

        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(x => x.Address.ToString())
            .FirstOrDefault(IsUsableIPv4) ?? string.Empty;
    }

    private static bool IsUsableIPv4(string ip)
    {
        return !ip.StartsWith("127.") &&
               !ip.StartsWith("169.254.") &&
               !ip.StartsWith("198.18.") &&
               !ip.StartsWith("192.168.50.");
    }
}

internal sealed record AuthResult(bool IsSuccess, string Message)
{
    public static AuthResult Ok(string message) => new(true, message);
    public static AuthResult Fail(string message) => new(false, message);
}

internal sealed class LoginConfig
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("callback")]
    public string Callback { get; set; } = "dr1004";

    [JsonPropertyName("preferred_profile")]
    public string PreferredProfile { get; set; } = "";

    [JsonPropertyName("profiles")]
    public List<LoginProfile> Profiles { get; set; } = [];
}

internal sealed class LoginProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("portal_ip")]
    public string PortalIp { get; set; } = "";

    [JsonPropertyName("match_prefixes")]
    public List<string> MatchPrefixes { get; set; } = [];

    [JsonPropertyName("maintenance_enabled")]
    public bool MaintenanceEnabled { get; set; }

    [JsonPropertyName("maintenance_start")]
    public string MaintenanceStart { get; set; } = "04:00";

    [JsonPropertyName("maintenance_end")]
    public string MaintenanceEnd { get; set; } = "04:15";
}

internal sealed class StatusResponse
{
    [JsonPropertyName("ss5")]
    public string? Ss5 { get; set; }
}

internal sealed class LoginResponse
{
    [JsonPropertyName("result")]
    public int Result { get; set; }

    [JsonPropertyName("ret_code")]
    public int RetCode { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}
