using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace MultiChatManager2
{
    public partial class YoudaoLoginWindow : Window
    {
        private const string YoudaoConsoleUrl =
            "https://ai.youdao.com/console/";

        private readonly string _youdaoProfileFolder;

        private bool _isInitialized;

        public YoudaoLoginWindow()
        {
            InitializeComponent();

            string dataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MultiChatManager2");

            _youdaoProfileFolder =
                Path.Combine(
                    dataFolder,
                    "YoudaoProfile");

            Directory.CreateDirectory(
                _youdaoProfileFolder);

            Loaded +=
                YoudaoLoginWindow_Loaded;

            Closed +=
                YoudaoLoginWindow_Closed;
        }

        public static string GetYoudaoProfileFolder()
        {
            string dataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MultiChatManager2");

            return Path.Combine(
                dataFolder,
                "YoudaoProfile");
        }

        private async void YoudaoLoginWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            try
            {
                LoginStatusTextBlock.Text =
                    "正在初始化有道智云登录环境...";

                LoadingBorder.Visibility =
                    Visibility.Visible;

                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: _youdaoProfileFolder);

                CoreWebView2ControllerOptions controllerOptions =
                    environment
                        .CreateCoreWebView2ControllerOptions();

                // 固定使用命名的持久档案，绝不使用 InPrivate 临时会话。
                // Cookie、Local Storage 等登录状态会保存在
                // %LOCALAPPDATA%\MultiChatManager2\YoudaoProfile 内。
                controllerOptions.ProfileName =
                    "YoudaoLogin";

                controllerOptions.IsInPrivateModeEnabled =
                    false;

                await YoudaoWebView
                    .EnsureCoreWebView2Async(
                        environment,
                        controllerOptions);

                ConfigureWebView();

                _isInitialized =
                    true;

                LoginStatusTextBlock.Text =
                    "正在检查有道智云登录状态...";

                YoudaoWebView.CoreWebView2.Navigate(
                    YoudaoConsoleUrl);
            }
            catch (Exception exception)
            {
                LoadingBorder.Visibility =
                    Visibility.Collapsed;

                LoginStatusTextBlock.Text =
                    "打开有道智云失败：" +
                    exception.Message;

                MessageBox.Show(
                    this,
                    "无法打开有道智云登录页面。\n\n" +
                    exception.Message,
                    "有道智云",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ConfigureWebView()
        {
            if (YoudaoWebView.CoreWebView2 == null)
            {
                return;
            }

            YoudaoWebView.CoreWebView2.Settings
                .AreDefaultContextMenusEnabled =
                true;

            YoudaoWebView.CoreWebView2.Settings
                .AreDevToolsEnabled =
                true;

            YoudaoWebView.CoreWebView2.Settings
                .IsStatusBarEnabled =
                false;

            YoudaoWebView.CoreWebView2.Settings
                .IsZoomControlEnabled =
                true;

            YoudaoWebView.CoreWebView2.Settings
                .AreBrowserAcceleratorKeysEnabled =
                true;

            YoudaoWebView.CoreWebView2
                .NavigationStarting +=
                CoreWebView2_NavigationStarting;

            YoudaoWebView.CoreWebView2
                .NavigationCompleted +=
                CoreWebView2_NavigationCompleted;

            YoudaoWebView.CoreWebView2
                .SourceChanged +=
                CoreWebView2_SourceChanged;

            YoudaoWebView.CoreWebView2
                .DocumentTitleChanged +=
                CoreWebView2_DocumentTitleChanged;
        }

        private void CoreWebView2_NavigationStarting(
            object? sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            LoadingBorder.Visibility =
                Visibility.Visible;

            LoginStatusTextBlock.Text =
                "正在加载有道智云页面...";
        }

        private async void CoreWebView2_NavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            LoadingBorder.Visibility =
                Visibility.Collapsed;

            if (!e.IsSuccess)
            {
                LoginStatusTextBlock.Text =
                    "页面加载失败：" +
                    e.WebErrorStatus;

                return;
            }

            await UpdateLoginStatusAsync();
        }

        private async void CoreWebView2_SourceChanged(
            object? sender,
            CoreWebView2SourceChangedEventArgs e)
        {
            await UpdateLoginStatusAsync();
        }

        private async void CoreWebView2_DocumentTitleChanged(
            object? sender,
            object e)
        {
            await UpdateLoginStatusAsync();
        }

        private async Task UpdateLoginStatusAsync()
        {
            if (YoudaoWebView.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                string currentUrl =
                    YoudaoWebView.Source?.ToString() ??
                    string.Empty;

                bool appearsLoggedIn =
                    await IsConsoleLoggedInAsync();

                if (appearsLoggedIn)
                {
                    LoginStatusTextBlock.Text =
                        "已检测到有道智云登录状态，可以点击“登录完成”。";

                    LoginCompletedButton.IsEnabled =
                        true;

                    return;
                }

                if (IsLoginPage(
                        currentUrl))
                {
                    LoginStatusTextBlock.Text =
                        "请在下方页面登录有道智云账号。";

                    LoginCompletedButton.IsEnabled =
                        false;

                    return;
                }

                LoginStatusTextBlock.Text =
                    "请完成有道智云登录，登录后进入控制台。";

                LoginCompletedButton.IsEnabled =
                    true;
            }
            catch
            {
                LoginStatusTextBlock.Text =
                    "请完成登录后点击“登录完成”。";

                LoginCompletedButton.IsEnabled =
                    true;
            }
        }

        private async Task<bool> IsConsoleLoggedInAsync()
        {
            if (YoudaoWebView.CoreWebView2 == null)
            {
                return false;
            }

            string currentUrl =
                YoudaoWebView.Source?.ToString() ??
                string.Empty;

            if (string.IsNullOrWhiteSpace(
                    currentUrl))
            {
                return false;
            }

            if (IsLoginPage(
                    currentUrl))
            {
                return false;
            }

            if (!currentUrl.Contains(
                    "ai.youdao.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string scriptResult =
                    await YoudaoWebView.CoreWebView2
                        .ExecuteScriptAsync(
                            """
                            (() => {
                                const text =
                                    document.body?.innerText || "";

                                const hasConsoleText =
                                    text.includes("业务指南") ||
                                    text.includes("财务管理") ||
                                    text.includes("应用总览") ||
                                    text.includes("资源包");

                                const hasLoginText =
                                    text.includes("账号登录") &&
                                    text.includes("密码");

                                return {
                                    hasConsoleText,
                                    hasLoginText,
                                    href: location.href
                                };
                            })();
                            """);

                if (scriptResult.Contains(
                        "\"hasConsoleText\":true",
                        StringComparison.OrdinalIgnoreCase) &&
                    !scriptResult.Contains(
                        "\"hasLoginText\":true",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            return currentUrl.Contains(
                       "/console",
                       StringComparison.OrdinalIgnoreCase) &&
                   !IsLoginPage(
                       currentUrl);
        }

        private static bool IsLoginPage(
            string url)
        {
            if (string.IsNullOrWhiteSpace(
                    url))
            {
                return false;
            }

            return url.Contains(
                       "login",
                       StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(
                       "reg.163.com",
                       StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(
                       "passport",
                       StringComparison.OrdinalIgnoreCase);
        }

        private async void ReloadButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isInitialized ||
                YoudaoWebView.CoreWebView2 == null)
            {
                await InitializeWebViewAsync();

                return;
            }

            LoginStatusTextBlock.Text =
                "正在重新加载页面...";

            YoudaoWebView.Reload();
        }

        private async void OpenConsoleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isInitialized ||
                YoudaoWebView.CoreWebView2 == null)
            {
                await InitializeWebViewAsync();

                return;
            }

            LoginStatusTextBlock.Text =
                "正在打开有道智云控制台...";

            YoudaoWebView.CoreWebView2.Navigate(
                YoudaoConsoleUrl);
        }

        private async void LoginCompletedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isInitialized ||
                YoudaoWebView.CoreWebView2 == null)
            {
                MessageBox.Show(
                    this,
                    "有道智云页面尚未完成初始化。",
                    "有道智云",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            LoginCompletedButton.IsEnabled =
                false;

            LoginStatusTextBlock.Text =
                "正在确认登录状态...";

            bool isLoggedIn =
                await IsConsoleLoggedInAsync();

            if (!isLoggedIn)
            {
                LoginCompletedButton.IsEnabled =
                    true;

                LoginStatusTextBlock.Text =
                    "尚未检测到有效登录状态。";

                MessageBox.Show(
                    this,
                    "尚未检测到有道智云控制台登录状态。\n\n" +
                    "请先在下方页面完成登录，并进入有道智云控制台。",
                    "尚未登录",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            LoginStatusTextBlock.Text =
                "登录成功，会话已保存。";

            DialogResult =
                true;

            Close();
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                false;

            Close();
        }

        private void YoudaoLoginWindow_Closed(
            object? sender,
            EventArgs e)
        {
            if (YoudaoWebView.CoreWebView2 != null)
            {
                YoudaoWebView.CoreWebView2
                    .NavigationStarting -=
                    CoreWebView2_NavigationStarting;

                YoudaoWebView.CoreWebView2
                    .NavigationCompleted -=
                    CoreWebView2_NavigationCompleted;

                YoudaoWebView.CoreWebView2
                    .SourceChanged -=
                    CoreWebView2_SourceChanged;

                YoudaoWebView.CoreWebView2
                    .DocumentTitleChanged -=
                    CoreWebView2_DocumentTitleChanged;
            }

            YoudaoWebView.Dispose();
        }
    }
}
