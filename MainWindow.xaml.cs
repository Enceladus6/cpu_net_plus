using cpu_net.Model;
using cpu_net.ViewModel;
using cpu_net.Views.Pages;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Permissions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Timer = System.Threading.Timer;

namespace cpu_net
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        BrushConverter brushConverter = new BrushConverter();
        Brush darkblue;
        Brush white;
        HomePage homePage = new HomePage();
        ConfigurationPage configurationPage = new ConfigurationPage();
        private MainViewModel _vm = new MainViewModel();
        public MainWindow()
        {
            InitializeComponent();
            homePage.ParentWindow = this;
            configurationPage.ParentWindow = this;
            ChangePage("home");
            //Debug.WriteLine("action1");
            TimerMain();
            //Debug.WriteLine("action2");
            SettingModel settingData = new SettingModel();
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                if (settingData.IsAutoLogin)
                {
                    loginToast();
                }
                if (settingData.IsAutoMin)
                {
                    this.Visibility = Visibility.Hidden;
                }
            }
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
                _vm.Record($"重连中，当前IP为{localIP}");
            }
            catch
            {
                _vm.Record("重连中，IP获取失败，请检查网络连接");
            }
            return localIP;
        }
        private async void LoginCheck(object? ob)
        {
            timer.Dispose(); // 销毁当前定时器
            SettingModel settingData = new SettingModel();
            string test_url = "8.8.8.8";
            string test_code = string.Empty;
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                test_url = settingData.TestUrl;
                test_code = settingData.TestCode;
            }
            // 使用HttpClient检测网络连接状态
            bool networkAvailable = false;
            string expectedContent = "Sora connect test"; // 预期的文件内容
            try
            {
                /*   using (var ping = new System.Net.NetworkInformation.Ping())
                   {
                       var reply = ping.Send("www.baidu.com", 1000); // Ping百度，超时1秒
                       _vm.Record("正在检测网络");
                       networkAvailable = reply?.Status == System.Net.NetworkInformation.IPStatus.Success;
                   }*/
                using (HttpClient client = new HttpClient())
                {
                    // 设置一个合理的超时时间，例如5秒
                    client.Timeout = TimeSpan.FromSeconds(5);

                    _vm.Record($"正在访问 {test_url} 进行网络检测");

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
                            _vm.Record($"connecttest.txt 内容不匹配：'{content.Trim()}'");
                        }
                    }
                    else
                    {
                        _vm.Record($"访问 {test_url} 失败，HTTP状态码：{response.StatusCode}");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // 网络请求相关的异常（例如，DNS解析失败，连接超时等）
                _vm.Record($"网络请求异常：{ex.Message}");
                if (ex.InnerException != null)
                { 
                    _vm.Record(ex.InnerException.Message); 
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // HttpClient超时异常
                _vm.Record($"网络连接超时：{ex.Message}");
            }
            catch (Exception ex)
            {
                // 其他未知异常
                _vm.Record($"检测过程中发生未知异常：{ex.Message}");
            }

            if (networkAvailable)
            {
                _vm.Record("网络正常");
            }
            else
            {
                _vm.Record("网络异常");
                var _IP = GetIP();
                if (String.IsNullOrEmpty(_IP))
                {
                    _vm.Record("请检查网络连接后重试");
                    return;
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
                        if (_ip[0] == "10" & _ip[1] == "12")
                        {
                            _mode = 0;
                            _vm.Record("重连中，自动识别为宽带环境");
                        }
                        else
                        {
                            _mode = 1;
                            _vm.Info("重连中，自动识别为CPU环境");
                        }
                        break;
                }
                if (_mode == 0) {
                    loginCheck(); // 仅在断网时执行登录检查
                    _vm.Info("检测到网络断开连接，已尝试重连");
                }
                
            }

            TimerMain(); // 重新启动定时器
        }
        /*
        private void Test()
        {
            Action invokeAction = new Action(Test);
            if (!this.Dispatcher.CheckAccess())
            {
                this.Dispatcher.Invoke(DispatcherPriority.Send, invokeAction);
            }
            else
            {
                PageFrame.Refresh();
                Debug.WriteLine("tick2");
                Debug.WriteLine(PageFrame.Source.ToString());
            }
        }
        */
        private void ChangePage(string name)
        {
            darkblue = (Brush)brushConverter.ConvertFrom("DarkBlue");
            white = (Brush)brushConverter.ConvertFrom("White");
            switch (name)
            {
                case "home":
                    Home_Button.BorderBrush = darkblue;
                    Conf_Button.BorderBrush = white;
                    var home = new HomePage();
                    home.ParentWindow = this;
                    PageFrame.Content = home;
                    break;
                case "conf":
                    Home_Button.BorderBrush = white;
                    Conf_Button.BorderBrush = darkblue;
                    var conf = new ConfigurationPage();
                    conf.ParentWindow = this;
                    PageFrame.Content = conf;
                    break;
            }
        }
        private void loginCheck()
        {
            SettingModel settingData = new SettingModel();
            //Debug.WriteLine("action4");
            if (settingData.PathExist())
            {
                settingData = settingData.Read();
                if (settingData.IsSetLogin)
                {
                    //Debug.WriteLine("count");
                    //MainViewModel mainViewModel = new MainViewModel();
                    //mainViewModel.LoginOnline();

                    Action invokeAction = new Action(loginCheck);
                    if (this.Dispatcher.CheckAccess())
                    {
                        loginToast();
                        //homePage.LoginButton.Command.Execute(null);
                    }
                    else
                    {
                        this.Dispatcher.BeginInvoke(DispatcherPriority.Send, invokeAction);
                    }

                }
            }
        }
        public void loginToast()
        {
            int a = _vm.LoginOnline();
            //Debug.WriteLine("a="+a);
            //Debug.WriteLine(this.Visibility);
            if (a == 0 & this.Visibility == Visibility.Collapsed)
            {
                // Debug.WriteLine("toasttest");
                new ToastContentBuilder()
                    .AddText("登录失败")
                    .AddText("请检查网络设置")
                    .Show();
            }
            ChangePage("home");
        }

        public void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MessageBoxResult result = System.Windows.MessageBox.Show("是否退出？", "询问", MessageBoxButton.YesNo, MessageBoxImage.Question);

            //关闭窗口
            if (result == MessageBoxResult.Yes)
                e.Cancel = false;

            //不关闭窗口
            if (result == MessageBoxResult.No)
                e.Cancel = true;
        }
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Visibility = Visibility.Hidden;
            }
        }

        private void notifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            //鼠标左键，实现窗体最小化隐藏或显示窗体
            if (e.Button == MouseButtons.Left)
            {
                if (this.Visibility == Visibility.Visible)
                {
                    this.Visibility = Visibility.Hidden;
                    //解决最小化到任务栏可以强行关闭程序的问题。
                    this.ShowInTaskbar = false;//使Form不在任务栏上显示
                }
                else
                {
                    this.Visibility = Visibility.Visible;
                    //解决最小化到任务栏可以强行关闭程序的问题。
                    this.ShowInTaskbar = false;//使Form不在任务栏上显示
                    this.Activate();
                }
            }
            if (e.Button == MouseButtons.Right)
            {
                //object sender = new object();
                // EventArgs e = new EventArgs();
                exit_Click(sender, e);//触发单击退出事件
            }
        }
        // 退出选项
        private void exit_Click(object sender, EventArgs e)
        {
            if (System.Windows.MessageBox.Show("是否退出？",
                                               "询问",
                                                MessageBoxButton.YesNo,
                                                MessageBoxImage.Question,
                                                MessageBoxResult.Yes) == MessageBoxResult.Yes)
            {
                //System.Windows.Application.Current.Shutdown();
                System.Environment.Exit(0);
            }
        }

        private void Home_Button_Click(object sender, RoutedEventArgs e)
        {
            ChangePage("home");
        }

        private void Conf_Button_Click(object sender, RoutedEventArgs e)
        {
            ChangePage("conf");
        }
    }
}
