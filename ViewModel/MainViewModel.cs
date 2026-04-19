using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using cpu_net.Model;
using cpu_net.ViewModel.Base;
using Microsoft.Toolkit.Uwp.Notifications;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Net.WebRequestMethods;
using File = System.IO.File;
using Timer = System.Threading.Timer;

namespace cpu_net.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        SettingModel settingData = new SettingModel();
        public MainViewModel()
        {
            Debug.WriteLine("MainViewModel constructor called.");
            TimerMain();
        }
        private Timer timer;

        public void TimerMain()
        {
            //Debug.WriteLine("action3");
            SettingModel settingData = new SettingModel();
            int loginTime = 1000;
            //Debug.WriteLine("action4");
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                loginTime = settingData.LoginTime * 1000;
            }
            timer = new Timer(LoginCheck, "", loginTime, loginTime);
            //timer = new Timer(LoginCheck, mainViewModel, 3000, 21600000);
            //timer.Dispose();
        }
        private async void LoginCheck(object? ob)
        {
            SettingModel settingData = new SettingModel();
            string test_url = "8.8.8.8";
            string test_code = string.Empty;
            bool _isSetLogin = false;
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                _isSetLogin = settingData.IsSetLogin;
                test_url = settingData.TestUrl;
                test_code = settingData.TestCode;
            }
            if (!_isSetLogin)
            {
                return;
            }
            // 使用HttpClient检测网络连接状态
            bool networkAvailable = false;
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // 设置一个合理的超时时间，例如5秒
                    client.Timeout = TimeSpan.FromSeconds(5);

                    // Record($"正在访问 {test_url} 进行网络检测");

                    HttpResponseMessage response = await client.GetAsync(test_url);

                    if (response.IsSuccessStatusCode) // 检查HTTP状态码是否为2xx成功
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        if (content.Trim() == test_code) // 比较文件内容，去除可能的空白字符
                        {
                            networkAvailable = true;
                        }
                        else
                        {
                            Record($"connecttest.txt 内容不匹配：'{content.Trim()}'");
                        }
                    }
                    else
                    {
                        Record($"访问 {test_url} 失败，HTTP状态码：{response.StatusCode}");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // 网络请求相关的异常（例如，DNS解析失败，连接超时等）
                Record($"网络请求异常：{ex.Message}");
                if (ex.InnerException != null)
                {
                    Record(ex.InnerException.Message);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // HttpClient超时异常
                Record($"网络连接超时：{ex.Message}");
            }
            catch (Exception ex)
            {
                // 其他未知异常
                Record($"检测过程中发生未知异常：{ex.Message}");
            }

            if (networkAvailable)
            {
                // Record("网络正常");
            }
            else
            {
                LoginOnline();
                Record("检测到网络断开连接，已尝试重连");

            }
        }

        private static readonly ReaderWriterLockSlim LogWriteLock = new ReaderWriterLockSlim();
        public void TextLog(string log, string LogName)
        {
            var now = DateTime.Now;
            if (settingData.PathExist())
            {
                settingData.Read();
            }
            if (!settingData.TestMode && LogName == "RecordLog")
            {
                return;
            }
            if (!Directory.Exists(LogName))
            {
                Directory.CreateDirectory(LogName);
            }
            string fileName = $"{now.Year}{now.Month:D2}.log"; // 格式化为两位数月份
            if (LogName == "RecordLog")
            {
                fileName = $"{now.Year}{now.Month:D2}{now.Day:D2}.log"; // 格式化为两位数月份
            }
            string logpath = Path.Combine(LogName, fileName);
            var _log = $"{DateTime.Now.ToString("M-d HH:mm:ss")}  " + log + "\r\n";
            try
            {
                //设置读写锁为写入模式独占资源，其他写入请求需要等待本次写入结束之后才能继续写入
                LogWriteLock.EnterWriteLock();
                File.AppendAllText(logpath, _log);
            }
            finally
            {
                //退出写入模式，释放资源占用
                LogWriteLock.ExitWriteLock();
            }
        }

        public string ReadLog(string LogName)
        {   
            var now = DateTime.Now;
            string fLog = "";
            string fileName = $"{now.Year}{now.Month:D2}.log"; // 格式化为两位数月份
            string logpath = Path.Combine(LogName, fileName);
            if (File.Exists(logpath))
            {

                try
                {
                    LogWriteLock.EnterReadLock();
                    // fLog = File.ReadAllText(logpath);
                    var lines = File.ReadLines(logpath);
                    var last10Lines = lines.Skip(Math.Max(0, lines.Count() - 10)).ToArray();
                    fLog = string.Join(Environment.NewLine, last10Lines);
                }
                finally
                {
                    LogWriteLock.ExitReadLock();
                }
            }
            else
            {
                return "";
            }
            return fLog;
        }

        public string TxtLog
        {
            get { return ReadLog("Log"); }
            set { TextLog(value, "Log"); OnPropertyChanged(); }
        }
        public string RecordLog
        {
            get { return ReadLog("RecordLog"); }
            set { TextLog(value, "RecordLog"); OnPropertyChanged(); }
        }

        public void Info(string message)
        {
            TxtLog = message;
        }
        public void Record(string message)
        {
            RecordLog = message;
        }

        private RelayCommand noticeButton_Click;
        public RelayCommand NoticeButton_Click
        {
            get
            {
                if (noticeButton_Click == null)
                    noticeButton_Click = new RelayCommand(() => NoticeOnline());
                return noticeButton_Click;

            }
            set { loginButton_Click = value; }
        }
        private RelayCommand loginButton_Click;
        public RelayCommand LoginButton_Click
        {
            get
            {
                if (loginButton_Click == null)
                    loginButton_Click = new RelayCommand(() => LoginOnline());
                return loginButton_Click;

            }
            set { loginButton_Click = value; }
        }

        private RelayCommand bindButton_Click;
        public RelayCommand BindButton_Click
        {
            get
            {
                if (bindButton_Click == null)
                    bindButton_Click = new RelayCommand(() => BindOnline());
                return bindButton_Click;

            }
            set { bindButton_Click = value; }
        }

        public string GetIP()
        {
            string localIP = string.Empty;
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIP = endPoint.Address.ToString();
                }
                Info($"当前IP为{localIP}");
                Record($"当前IP为{localIP}");
            }
            catch
            {
                Info("IP获取失败");
                Record("IP获取失败");
            }
            return localIP;
        }

        public class _LRes
        {
            public int result { get; set; }
        }
        public class _lRes
        {
            public string result { get; set; }
        }

        public class V46ip
        {
            public string ss5 { get; set; }
        }
        public class ret
        {
            public int ret_code { get; set; }
        }
        public class LRes
        {
            public string msga { get; set; }
        }
        public class lRes
        {
            public string msg { get; set; }
        }
        public int LoginOnline()
        {
            if (settingData.PathExist())
            {
                var _IP = GetIP();
                if (String.IsNullOrEmpty(_IP))
                {
                    Info("请检查网络连接后重试");
                    return 0;
                }
                settingData = settingData.Read();
                int _mode = 0;
                switch (settingData.Mode)
                {
                    case 0:
                        _mode = settingData.Mode;
                        break;
                    case 1:
                        _mode = settingData.Mode;
                        break;
                    case 2:
                        string[] _ip = _IP.Split('.');
                        if (_ip[0] == "10" && _ip[1] == "12")
                        {
                            _mode = 0;
                            Info("自动识别为宽带环境");
                        }
                        else if (_ip[0] == "10" && _ip[1] == "31")
                        {
                            _mode = 0;
                            Info("自动识别为宽带环境");
                        }
                        else if (_ip[0] == "10" && _ip[1] == "33")
                        {
                            _mode = 0;
                            Info("自动识别为宽带环境");
                        }
                        else if (_ip[0] == "192")
                        {
                            _mode = 0;
                            Info("自动识别为宽带环境");
                        }
                        else
                        {
                            _mode = 1;
                            Info("自动识别为CPU环境");
                        }
                        break;
                }
                string Login_url;
                string _res = "";
                string local_ip;
                string url_head = "http://172.17.253.3:801/eportal/portal/login?";
                string url_head_ssl = "https://172.17.253.3:802/eportal/portal/login?";
                switch (_mode)
                {
                    case 0:
                        
                        try
                        {
                            string remote_url = "http://172.17.253.3/drcom/chkstatus?callback=dr1002";
                            string res_remo = HttpRequestHelper.HttpGetRequest(remote_url).Replace("dr1002", "").Replace(" ", "");
                            var res = res_remo.Substring(1, res_remo.Length - 2);
                            var _obj = JsonSerializer.Deserialize<V46ip>(res)!;
                            local_ip = _obj.ss5;
                        }
                        catch (Exception e)
                        {
                            Record("Mode Case 0");
                            Record(e.Message);
                            local_ip = _IP;
                        }
                        Login_url = $"{url_head}callback=dr1004&login_method=1&user_account=%2C0%2C{WebUtility.UrlEncode(settingData.Username)}%40{WebUtility.UrlEncode(settingData.Carrier)}" +
                    $"&user_password={WebUtility.UrlEncode(settingData.Password)}&wlan_user_ip={WebUtility.UrlEncode(local_ip)}&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=4.2.2&terminal_type=1&lang=zh-cn&v=9745&lang=zh";
                        break;
                    case 1:
                        try
                        {
                            string remote_url = "http://192.168.199.21/drcom/chkstatus?callback=dr1002";
                            string res_remo = HttpRequestHelper.HttpGetRequest(remote_url).Replace("dr1002", "").Replace(" ", "");
                            var res = res_remo.Substring(1, res_remo.Length - 2);
                            var _obj = JsonSerializer.Deserialize<V46ip>(res)!;
                            local_ip = _obj.ss5;
                        }
                        catch (Exception e)
                        {
                            Record("Mode Case 1");
                            Record(e.Message);
                            local_ip = _IP;
                        }
                        //Info(local_ip);
                        Login_url = $"http://192.168.199.21:801/eportal/?c=Portal&a=login&callback=dr1004&login_method=1&user_account=%2C0%2C{WebUtility.UrlEncode(settingData.Username)}&user_password={WebUtility.UrlEncode(settingData.Password)}" +
                            $"&wlan_user_ip={WebUtility.UrlEncode(local_ip)}&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=3.3.3&v=1954";
                        break;
                    default:
                        try
                        {
                            string remote_url = "http://172.17.253.3/drcom/chkstatus?callback=dr1002";
                            string res_remo = HttpRequestHelper.HttpGetRequest(remote_url).Replace("dr1002", "").Replace(" ", "");
                            var res = res_remo.Substring(1, res_remo.Length - 2);
                            var _obj = JsonSerializer.Deserialize<V46ip>(res)!;
                            local_ip = _obj.ss5;
                        }
                        catch (Exception e)
                        {
                            Record("Mode Case default");
                            Record(e.Message);
                            local_ip = _IP;
                        }
                        Login_url = $"{url_head}callback=dr1004&login_method=1&user_account=%2C0%2C{WebUtility.UrlEncode(settingData.Username)}%40{WebUtility.UrlEncode(settingData.Carrier)}" +
                    $"&user_password={WebUtility.UrlEncode(settingData.Password)}&wlan_user_ip={WebUtility.UrlEncode(local_ip)}&wlan_user_ipv6=&wlan_user_mac=000000000000&wlan_ac_ip=&wlan_ac_name=&jsVersion=4.2.2&lang=zh-cn&v=9745&lang=zh";
                        break;
                }
                Record(MaskSensitive(Login_url));
                try
                {
                    //var _res = HttpRequestHelper.HttpGetRequest(Login_url).Replace(" ","");
                    //Info(Login_url);

                    _res = HttpRequestHelper.HttpGetRequest(Login_url);
                    //Info(_res);
                    _res = _res.Replace("dr1004", "").Replace(" ", "");
                    Record(_res);
                    //Info(_res);
                    //System.Diagnostics.Debug.WriteLine(_res);
                    var res = _res.Substring(1, _res.Length - 3);
                    //Info(res);
                    //System.Diagnostics.Debug.WriteLine(res);
                    if (res == null)
                    {
                        Info("网络错误");
                        return 0;
                    }

                    var _Obj = JsonSerializer.Deserialize<_LRes>(res)!;
                    if (_Obj != null)
                    {
                        switch (_Obj.result)
                        {
                            case 1:
                                Info("登录成功");
                                return 1;
                            case 0:
                                var obj = JsonSerializer.Deserialize<ret>(res)!;
                                switch (obj.ret_code)
                                {
                                    default:
                                        Info("登录失败");
                                        var msg = JsonSerializer.Deserialize<lRes>(res)!;
                                        Info($"Error Message: {msg.msg}");
                                        return 0;
                                    case 2:
                                        Info("本设备已在线，请勿重复登录");
                                        return 0;
                                }
                        }
                    }

                }
                catch (HttpRequestException e)
                {
                    Info("登录失败");
                    Info(e.Message);
                    Record(e.Message);
                    return 0;
                }
                catch (JsonException e)
                {
                    Record(e.Message);
                    Info("JSON解析失败");
                    return 0;
                    //Info(_res);
                }
                catch(Exception e){
                    Record(e.TargetSite + e.Message + e.StackTrace);
                    Info(e.TargetSite+e.Message+e.StackTrace);
                    Info("网络连接失败，请检查网络设置，如果使用路由器，请确认是否使用自动获取ip");
                    return 0;
                }
            }
            else
            {
                Info("No Config Found");
                return 0;
                //var result = MessageBox.Show("请在设置中添加账号信息", "提示");
                /*
                if (result == MessageBoxResult.OK)
                {
                    ConfSel();
                }
                
                Info(pageModel.PageName);
                */
            }
            return 0;
        }

        private static string MaskSensitive(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return Regex.Replace(text, @"(user_password=)([^&]*)", "$1***", RegexOptions.IgnoreCase);
        }

        private void NoticeOnline()
        {
            System.Diagnostics.Process.Start("explorer.exe", "https://lic.cpu.edu.cn/ee/c6/c7550a192198/page.htm");
        }
        private void BindOnline()
        {
            System.Diagnostics.Process.Start("explorer.exe", "http://192.168.199.70:8080/Self/Dashboard");
        }
    }

    public class LoginInfoViewModel : BindableBase
    {
        public LoginInfoViewModel(SettingModel settingModel)
        {
            _SettingModel = settingModel;
            SettingModel.PropertyChanged += (object sender, PropertyChangedEventArgs e) => settingModel.Save();
        }
        private SettingModel _SettingModel;

        public SettingModel SettingModel
        {
            get { return _SettingModel; }
            set { SetProperty(ref _SettingModel, value); }
        }
    }

    public class UserViewModel : ViewModelBase
    {
        SettingModel settingData = new SettingModel();
        AutoStart autoStart = new AutoStart();
        public UserViewModel()
        {
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                Code = settingData.Username;
                Password = settingData.Password;
                IsAutoRun = settingData.IsAutoRun;
                IsAutoLogin = settingData.IsAutoLogin;
                IsAutoMin = settingData.IsAutoMin;
                IsSetLogin = settingData.IsSetLogin;
                Mode = settingData.Mode;
                LoginTime = settingData.LoginTime;
            }
        }

        private string code;
        public string Code
        {
            get { return code; }
            set { code = value; OnPropertyChanged(); }
        }

        private string password;
        public string Password
        {
            get { return password; }
            set { password = value; OnPropertyChanged(); }
        }

        private bool isAutoRun;
        public bool IsAutoRun
        {
            get { return isAutoRun; }
            set
            {
                isAutoRun = value; settingData.IsAutoRun = isAutoRun;
                autoStart.SetMeAutoStart(isAutoRun);
                OnPropertyChanged();
            }
        }

        private bool isAutoLogin;
        public bool IsAutoLogin
        {
            get { return isAutoLogin; }
            set { isAutoLogin = value; settingData.IsAutoLogin = isAutoLogin; OnPropertyChanged(); }
        }

        private bool isAutoMin;
        public bool IsAutoMin
        {
            get { return isAutoMin; }
            set { isAutoMin = value; settingData.IsAutoMin = isAutoMin; OnPropertyChanged(); }
        }

        private bool isSetLogin;
        public bool IsSetLogin
        {
            get { return isSetLogin; }
            set { isSetLogin = value; settingData.IsSetLogin = isSetLogin; OnPropertyChanged(); }
        }

        private int mode;
        public int Mode
        {
            get { return mode; }
            set { mode = value; settingData.Mode = mode; OnPropertyChanged(); }
        }

        private int loginTime;
        public int LoginTime
        {
            get { return loginTime; }
            set { loginTime = value; settingData.LoginTime = loginTime; OnPropertyChanged(); }
        }

        private RelayCommand pppButton_Click;
        public RelayCommand PppButton_Click
        {
            get
            {
                if (pppButton_Click == null)
                    pppButton_Click = new RelayCommand(() => PppMode());
                return pppButton_Click;

            }
            set { pppButton_Click = value; }
        }

        private RelayCommand cpuButton_Click;
        public RelayCommand CpuButton_Click
        {
            get
            {
                if (cpuButton_Click == null)
                    cpuButton_Click = new RelayCommand(() => CpuMode());
                return cpuButton_Click;

            }
            set { cpuButton_Click = value; }
        }

        private RelayCommand autoButton_Click;
        public RelayCommand AutoButton_Click
        {
            get
            {
                if (autoButton_Click == null)
                    autoButton_Click = new RelayCommand(() => AutoMode());
                return autoButton_Click;

            }
            set { autoButton_Click = value; }
        }

        private void PppMode()
        {
            Mode = 0;
        }

        private void CpuMode()
        {
            Mode = 1;
        }

        private void AutoMode()
        {
            Mode = 2;
        }
    }

    public class CarrierViewModel : ViewModelBase
    {
        SettingModel settingData = new SettingModel();
        public CarrierViewModel()
        {
            ComboxList = new ObservableCollection<CarriersModel>() {
          new CarriersModel() { Key = 0,Text = "请选择运营商" },
          new CarriersModel() { Key = 1,Text = "移动" },
          new CarriersModel() { Key = 2,Text = "联通" },
          new CarriersModel() { Key = 3,Text = "电信" },
        };
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                ComboxItem = ComboxList[settingData.Key];
            }
            else
            {
                ComboxItem = ComboxList[0];
            }

        }

        private CarriersModel comboxItem;
        /// <summary>
        /// 下拉框选中信息
        /// </summary>
        public CarriersModel ComboxItem
        {
            get { return comboxItem; }
            set
            {
                comboxItem = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<CarriersModel> comboxList;
        /// <summary>
        /// 下拉框列表
        /// </summary>
        public ObservableCollection<CarriersModel> ComboxList
        {
            get { return comboxList; }
            set { comboxList = value; OnPropertyChanged(); }
        }
    }

    //绑定PasswordBox
    public static class LoginPasswordBoxHelper
    {
        public static string GetPassword(DependencyObject obj)
        {
            return (string)obj.GetValue(PasswordProperty);
        }

        public static void SetPassword(DependencyObject obj, string value)
        {
            obj.SetValue(PasswordProperty, value);
        }

        public static readonly DependencyProperty PasswordProperty =
            DependencyProperty.RegisterAttached("Password", typeof(string), typeof(LoginPasswordBoxHelper), new PropertyMetadata(""));
        public static bool GetIsPasswordBindingEnable(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsPasswordBindingEnableProperty);
        }

        public static void SetIsPasswordBindingEnable(DependencyObject obj, bool value)
        {
            obj.SetValue(IsPasswordBindingEnableProperty, value);
        }

        public static readonly DependencyProperty IsPasswordBindingEnableProperty =
            DependencyProperty.RegisterAttached("IsPasswordBindingEnable", typeof(bool), typeof(LoginPasswordBoxHelper),
                                                new FrameworkPropertyMetadata(OnIsPasswordBindingEnabledChanged));

        private static void OnIsPasswordBindingEnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            var passwordBox = obj as PasswordBox;
            if (passwordBox != null)
            {
                passwordBox.PasswordChanged -= PasswordBoxPasswordChanged;
                if ((bool)e.NewValue)
                {
                    passwordBox.PasswordChanged += PasswordBoxPasswordChanged;
                }
            }
        }

        static void PasswordBoxPasswordChanged(object sender, RoutedEventArgs e)
        {
            var passwordBox = (PasswordBox)sender;
            if (!String.Equals(GetPassword(passwordBox), passwordBox.Password))
            {
                SetPassword(passwordBox, passwordBox.Password);
            }
        }

    }

    public static class HttpRequestHelper
    {
        /// <summary>
        /// Http Get Request
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static string HttpGetRequest(string url)
        {
            try
            {
                string strGetResponse = string.Empty;
                var getRequest = CreateHttpRequest(url, "GET");
                var getResponse = getRequest.GetResponse() as HttpWebResponse;
                strGetResponse = GetHttpResponse(getResponse, "GET");
                return strGetResponse;
            }
            catch (Exception ex) 
            {
                return string.Empty;
                // return ex.TargetSite+ex.Message+ex.StackTrace;
            }
        }

        /// <summary>
        /// Http Post Request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="postJsonData"></param>
        /// <returns></returns>
        public static string HttpPostRequest(string url, string postJsonData)
        {
            string strPostReponse = string.Empty;
            try
            {
                var postRequest = CreateHttpRequest(url, "POST", postJsonData);
                var postResponse = postRequest.GetResponse() as HttpWebResponse;
                strPostReponse = GetHttpResponse(postResponse, "POST");
            }
            catch (Exception ex)
            {
                strPostReponse = ex.Message;
            }
            return strPostReponse;
        }


        private static HttpWebRequest CreateHttpRequest(string url, string requestType, params object[] strJson)
        {
            HttpWebRequest request = null;
            const string get = "GET";
            const string post = "POST";
            if (string.Equals(requestType, get, StringComparison.OrdinalIgnoreCase))
            {
                request = CreateGetHttpWebRequest(url);
            }
            if (string.Equals(requestType, post, StringComparison.OrdinalIgnoreCase))
            {
                request = CreatePostHttpWebRequest(url, strJson[0].ToString());
            }
            return request;
        }

        private static HttpWebRequest CreateGetHttpWebRequest(string url)
        {
            var getRequest = HttpWebRequest.Create(url) as HttpWebRequest;
            getRequest.Method = "GET";
            getRequest.Timeout = 5000;
            getRequest.ContentType = "text/html;charset=UTF-8";
            getRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return getRequest;
        }

        private static HttpWebRequest CreatePostHttpWebRequest(string url, string postData)
        {
            var postRequest = HttpWebRequest.Create(url) as HttpWebRequest;
            postRequest.KeepAlive = false;
            postRequest.Timeout = 5000;
            postRequest.Method = "POST";
            postRequest.ContentType = "application/x-www-form-urlencoded";
            postRequest.ContentLength = postData.Length;
            postRequest.AllowWriteStreamBuffering = false;
            StreamWriter writer = new StreamWriter(postRequest.GetRequestStream(), Encoding.ASCII);
            writer.Write(postData);
            writer.Flush();
            return postRequest;
        }

        private static string GetHttpResponse(HttpWebResponse response, string requestType)
        {
            var responseResult = "";
            const string post = "POST";
            string encoding = "UTF-8";
            if (string.Equals(requestType, post, StringComparison.OrdinalIgnoreCase))
            {
                encoding = response.ContentEncoding;
                if (encoding == null || encoding.Length < 1)
                {
                    encoding = "UTF-8";
                }
            }
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding(encoding)))
            {
                responseResult = reader.ReadToEnd();
            }
            return responseResult;
        }

        private static string GetHttpResponseAsync(HttpWebResponse response, string requestType)
        {
            var responseResult = "";
            const string post = "POST";
            string encoding = "UTF-8";
            if (string.Equals(requestType, post, StringComparison.OrdinalIgnoreCase))
            {
                encoding = response.ContentEncoding;
                if (encoding == null || encoding.Length < 1)
                {
                    encoding = "UTF-8";
                }
            }
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding(encoding)))
            {
                responseResult = reader.ReadToEnd();
            }
            return responseResult;
        }
    }
}
