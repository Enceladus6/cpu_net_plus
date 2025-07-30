using cpu_net.Model;
using cpu_net.ViewModel;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace cpu_net.Views.Pages
{
    /// <summary>
    /// HomePage.xaml 的交互逻辑
    /// </summary>
    /// 
    public partial class HomePage : Page
    {
        SettingModel settingData = new SettingModel();
        private readonly HttpClient _httpClient;
        readonly String DefaultNoticeText = @"欢迎使用本程序，
本程序旨在帮助药大学生自动登录校园网，免受断网困扰
本本答疑群：939789212
使用本程序前，需要绑定运营商账号，
绑定方法点击下方按钮可以显示
本程序所有数据均存放在本地，
如有使用困难请带着日志截图去本本群询问
注意事项：
如果需要自动登录功能，请勾选定时登录
其功能为固定间隔检测联网状态
设置中的自动登录为启动时自动登录
需要电脑不关机并连宿舍网
尝试使用CPU模式强制连接CPU时可能会卡住，是正常现象";

        MainWindow parentWindow;
        public MainWindow ParentWindow
        {
            get { return parentWindow; }
            set { parentWindow = value; }
        }

        public HomePage()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                if (parentWindow != null)
                {
                    this.DataContext = parentWindow.DataContext;
                }
            };
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5) // 设置超时时间
            };

            Loaded += HomePage_Loaded; // 页面加载时获取公告
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadNoticeText();
        }

        private async Task LoadNoticeText()
        {
            // 先显示默认公告
            textNotice.Text = DefaultNoticeText;
            sv1.ScrollToEnd();

            try
            {
                // 尝试从服务器获取最新公告
                string noticeUrl = "http://10.3.4.106/notice.txt";
                string noticeContent = await _httpClient.GetStringAsync(noticeUrl);

                if (!string.IsNullOrWhiteSpace(noticeContent))
                {
                    textNotice.Text = noticeContent;
                    sv1.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不中断用户体验
                // 在实际应用中，可以记录到日志系统
                Console.WriteLine($"加载公告失败: {ex.Message}");

                // 可选：在UI上显示错误信息
                // textNotice.Text = $"{DefaultNoticeText}\n\n[公告加载失败: {ex.Message}]";
            }
        }

        private void LoginButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (settingData.PathExist())
            {
                return;
            }
            else
            {
                var result = MessageBox.Show("请在设置中添加账号信息", "提示");

                if (result == MessageBoxResult.OK)
                {
                    ToConf();
                }

            }
        }
        private void ToConf()
        {

            Action invokeAction = new Action(ToConf);
            if (!this.Dispatcher.CheckAccess())
            {
                this.Dispatcher.Invoke(DispatcherPriority.Send, invokeAction);
            }
            else
            {
                this.parentWindow.Conf_Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                //this.NavigationService.Source = new Uri("/Views/Pages/ConfigurationPage.xaml", UriKind.Relative);
            }
        }

        private void textLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            sv1.ScrollToEnd();
        }
    }
}
