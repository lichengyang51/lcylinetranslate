using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class YoudaoQuotaWindow : Window
    {
        private const string YoudaoConsoleUrl =
            "https://ai.youdao.com/console/";

        private const string ResourcePageUrl =
            "https://ai.youdao.com/console/#/source-bundle-cata";

        private const string ResourceApiPath =
            "/consoleApi/resourcePackage/" +
            "getUserResourcePackageUsageOverviewPage";

        private const string YoudaoWebViewProfileName =
            "YoudaoLogin";

        private const int QuotaResponseTimeoutSeconds =
            15;

        private readonly string _profileFolder;

        private bool _initialized;

        private bool _querying;

        public YoudaoQuotaWindow()
        {
            InitializeComponent();

            _profileFolder =
                YoudaoLoginWindow
                    .GetYoudaoProfileFolder();

            Directory.CreateDirectory(
                _profileFolder);

            Loaded +=
                YoudaoQuotaWindow_Loaded;

            Closed +=
                YoudaoQuotaWindow_Closed;
        }

        private async void YoudaoQuotaWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshQuotaAsync(
                allowLoginWindow: true);
        }

        private async Task<bool> InitializeWebViewAsync()
        {
            if (_initialized &&
                QueryWebView.CoreWebView2 != null)
            {
                return true;
            }

            try
            {
                SyncStatusTextBlock.Foreground =
                    Brushes.Gray;

                SyncStatusTextBlock.Text =
                    "正在初始化有道查询环境...";

                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: _profileFolder);

                // 必须与 YoudaoLoginWindow 使用同一命名档案，
                // 才能共享有道登录 Cookie 和本地会话。
                CoreWebView2ControllerOptions controllerOptions =
                    environment
                        .CreateCoreWebView2ControllerOptions();

                controllerOptions.ProfileName =
                    YoudaoWebViewProfileName;

                controllerOptions.IsInPrivateModeEnabled =
                    false;

                await QueryWebView
                    .EnsureCoreWebView2Async(
                        environment,
                        controllerOptions);

                if (QueryWebView.CoreWebView2 == null)
                {
                    return false;
                }

                QueryWebView.CoreWebView2.Settings
                    .AreDefaultContextMenusEnabled =
                    false;

                QueryWebView.CoreWebView2.Settings
                    .AreDevToolsEnabled =
                    true;

                QueryWebView.CoreWebView2.Settings
                    .IsStatusBarEnabled =
                    false;

                QueryWebView.CoreWebView2.Settings
                    .AreBrowserAcceleratorKeysEnabled =
                    false;

                _initialized =
                    true;

                return true;
            }
            catch (Exception exception)
            {
                ShowError(
                    "有道查询环境初始化失败：" +
                    exception.Message);

                return false;
            }
        }

        private async Task RefreshQuotaAsync(
            bool allowLoginWindow)
        {
            if (_querying)
            {
                return;
            }

            _querying =
                true;

            SetBusyState(
                true,
                "正在连接有道智云...");

            try
            {
                bool initialized =
                    await InitializeWebViewAsync();

                if (!initialized)
                {
                    return;
                }

                QueryResult result =
                    await CaptureQuotaResponseAsync();

                if (result.Success &&
                    result.Quota != null)
                {
                    UpdateQuotaUi(
                        result.Quota);

                    SyncStatusTextBlock.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                4,
                                120,
                                87));

                    SyncStatusTextBlock.Text =
                        "已同步最新数据：" +
                        DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss");

                    return;
                }

                if (allowLoginWindow &&
                    result.RequiresLogin)
                {
                    bool loggedIn =
                        ShowLoginWindow();

                    if (loggedIn)
                    {
                        await RefreshQuotaAsyncAfterLogin();
                    }
                    else
                    {
                        ShowError(
                            "尚未登录有道智云。");
                    }

                    return;
                }

                ShowError(
                    string.IsNullOrWhiteSpace(
                        result.Message)
                        ? "没有获取到资源包数据。"
                        : result.Message);
            }
            catch (Exception exception)
            {
                ShowError(
                    "查询字符情况失败：" +
                    exception.Message);
            }
            finally
            {
                _querying =
                    false;

                SetButtonsEnabled(
                    true);
            }
        }

        private async Task RefreshQuotaAsyncAfterLogin()
        {
            SetBusyState(
                true,
                "登录成功，正在查询资源包...");

            /*
             * 登录窗口使用相同的 WebView2 Profile。
             * 等待浏览器把 Cookie 和本地会话写入磁盘。
             */
            await Task.Delay(
                1200);

            QueryResult result =
                await CaptureQuotaResponseAsync();

            if (result.Success &&
                result.Quota != null)
            {
                UpdateQuotaUi(
                    result.Quota);

                SyncStatusTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            4,
                            120,
                            87));

                SyncStatusTextBlock.Text =
                    "已同步最新数据：" +
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss");

                return;
            }

            ShowError(
                string.IsNullOrWhiteSpace(
                    result.Message)
                    ? "登录成功，但没有获取到资源包数据。"
                    : result.Message);
        }

        private async Task<QueryResult> CaptureQuotaResponseAsync()
        {
            if (QueryWebView.CoreWebView2 == null)
            {
                return QueryResult.Failed(
                    "有道查询浏览器尚未初始化。");
            }

            TaskCompletionSource<QueryResult>
                completionSource =
                    new TaskCompletionSource<QueryResult>(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);

            /*
             * 有道控制台并没有公开资源包查询 API；页面的内部接口
             * 可能会随着控制台升级而变更。记录本次实际请求到的
             * consoleApi 路径，若旧接口没有出现，就把诊断结果显示出来，
             * 方便后续精准更新接口，而不是无意义地等待超时。
             */
            HashSet<string> observedConsoleApiPaths =
                new(StringComparer.OrdinalIgnoreCase);

            object observedPathsLock =
                new();

            async void ResponseReceivedHandler(
                object? sender,
                CoreWebView2WebResourceResponseReceivedEventArgs e)
            {
                try
                {
                    string requestUrl =
                        e.Request.Uri ??
                        string.Empty;

                    if (Uri.TryCreate(
                            requestUrl,
                            UriKind.Absolute,
                            out Uri? requestUri) &&
                        requestUri.AbsolutePath.Contains(
                            "/consoleApi/",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        lock (observedPathsLock)
                        {
                            observedConsoleApiPaths.Add(
                                requestUri.AbsolutePath);
                        }
                    }

                    if (!requestUrl.Contains(
                            ResourceApiPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    int statusCode =
                        e.Response.StatusCode;

                    if (statusCode == 401 ||
                        statusCode == 403)
                    {
                        completionSource.TrySetResult(
                            QueryResult.LoginRequired(
                                "有道登录状态已失效。"));

                        return;
                    }

                    if (statusCode < 200 ||
                        statusCode >= 300)
                    {
                        completionSource.TrySetResult(
                            QueryResult.Failed(
                                "有道服务器请求失败，状态码：" +
                                statusCode));

                        return;
                    }

                    using Stream contentStream =
                        await e.Response
                            .GetContentAsync();

                    using StreamReader reader =
                        new StreamReader(
                            contentStream,
                            Encoding.UTF8);

                    string responseJson =
                        await reader.ReadToEndAsync();

                    QuotaInfo? quota =
                        ParseQuota(
                            responseJson);

                    if (quota == null)
                    {
                        bool looksLoggedOut =
                            responseJson.Contains(
                                "未登录",
                                StringComparison.OrdinalIgnoreCase) ||
                            responseJson.Contains(
                                "登录",
                                StringComparison.OrdinalIgnoreCase);

                        completionSource.TrySetResult(
                            looksLoggedOut
                                ? QueryResult.LoginRequired(
                                    "有道登录状态已失效。")
                                : QueryResult.Failed(
                                    "无法识别有道服务器返回的数据。"));

                        return;
                    }

                    completionSource.TrySetResult(
                        QueryResult.Succeeded(
                            quota));
                }
                catch (Exception exception)
                {
                    completionSource.TrySetResult(
                        QueryResult.Failed(
                            "读取有道响应失败：" +
                            exception.Message));
                }
            }

            void NavigationCompletedHandler(
                object? sender,
                CoreWebView2NavigationCompletedEventArgs e)
            {
                /*
                 * 有道控制台是单页应用。跳转到带 # 的内部页面时，
                 * 浏览器经常主动取消前一个导航并报告 ConnectionAborted；
                 * 这不代表网络失败，仍需继续等待实际的资源包 API。
                 */
                if (!e.IsSuccess &&
                    e.WebErrorStatus !=
                    CoreWebView2WebErrorStatus.ConnectionAborted)
                {
                    completionSource.TrySetResult(
                        QueryResult.Failed(
                            "有道资源页面加载失败：" +
                            e.WebErrorStatus));
                }
            }

            QueryWebView.CoreWebView2
                .WebResourceResponseReceived +=
                ResponseReceivedHandler;

            QueryWebView.CoreWebView2
                .NavigationCompleted +=
                NavigationCompletedHandler;

            try
            {
                SyncStatusTextBlock.Foreground =
                    Brushes.Gray;

                SyncStatusTextBlock.Text =
                    "正在获取有道官方资源包数据...";

                string loadedUrl =
                    QueryWebView.Source?.ToString() ??
                    string.Empty;

                /*
                 * 首次打开时进入资源包页面；后续点击“刷新”时，
                 * 相同地址的 Navigate 不会让单页应用重新请求数据，
                 * 因此必须使用 Reload 强制发起一次新的接口请求。
                 */
                if (loadedUrl.StartsWith(
                        ResourcePageUrl,
                        StringComparison.OrdinalIgnoreCase))
                {
                    QueryWebView.CoreWebView2.Reload();
                }
                else
                {
                    QueryWebView.CoreWebView2.Navigate(
                        ResourcePageUrl);
                }

                Task finishedTask =
                    await Task.WhenAny(
                        completionSource.Task,
                        Task.Delay(
                            TimeSpan.FromSeconds(
                                QuotaResponseTimeoutSeconds)));

                if (finishedTask !=
                    completionSource.Task)
                {
                    string currentUrl =
                        QueryWebView.Source?.ToString() ??
                        string.Empty;

                    bool redirectedToLogin =
                        IsLoginUrl(
                            currentUrl);

                    return redirectedToLogin
                        ? QueryResult.LoginRequired(
                            "请先登录有道智云。")
                        : CreateQuotaTimeoutResult(
                            observedConsoleApiPaths,
                            observedPathsLock);
                }

                return await completionSource.Task;
            }
            finally
            {
                QueryWebView.CoreWebView2
                    .WebResourceResponseReceived -=
                    ResponseReceivedHandler;

                QueryWebView.CoreWebView2
                    .NavigationCompleted -=
                    NavigationCompletedHandler;
            }
        }

        private static QueryResult CreateQuotaTimeoutResult(
            HashSet<string> observedConsoleApiPaths,
            object observedPathsLock)
        {
            StringBuilder paths =
                new StringBuilder();

            lock (observedPathsLock)
            {
                int count = 0;

                foreach (string path in
                         observedConsoleApiPaths)
                {
                    if (count++ == 3)
                    {
                        break;
                    }

                    if (paths.Length > 0)
                    {
                        paths.Append("；");
                    }

                    paths.Append(path);
                }
            }

            return paths.Length == 0
                ? QueryResult.Failed(
                    "资源包页面没有发出可识别的有道接口请求。")
                : QueryResult.Failed(
                    "未检测到旧版资源包接口。实际访问：" +
                    paths);
        }

        private static QuotaInfo? ParseQuota(
            string responseJson)
        {
            if (string.IsNullOrWhiteSpace(
                    responseJson))
            {
                return null;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(
                        responseJson);

                return FindQuota(
                    document.RootElement);
            }
            catch
            {
                return null;
            }
        }

        private static QuotaInfo? FindQuota(
            JsonElement element)
        {
            if (element.ValueKind ==
                JsonValueKind.Object)
            {
                string name =
                    GetString(
                        element,
                        "name");

                if (!string.IsNullOrWhiteSpace(
                        name) &&
                    name.Contains(
                        "文本翻译",
                        StringComparison.OrdinalIgnoreCase))
                {
                    long total =
                        GetInt64(
                            element,
                            "spec");

                    long remaining =
                        GetInt64(
                            element,
                            "balance");

                    if (total > 0)
                    {
                        return new QuotaInfo
                        {
                            Name =
                                name,

                            Total =
                                total,

                            Remaining =
                                remaining,

                            Used =
                                Math.Max(
                                    0,
                                    total - remaining),

                            Percent =
                                remaining * 100.0 /
                                total,

                            Status =
                                GetString(
                                    element,
                                    "packageStatus"),

                            EffectiveTime =
                                GetString(
                                    element,
                                    "createTime"),

                            ExpireTime =
                                GetString(
                                    element,
                                    "expiredTime")
                        };
                    }
                }

                foreach (JsonProperty property in
                         element.EnumerateObject())
                {
                    QuotaInfo? result =
                        FindQuota(
                            property.Value);

                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            else if (element.ValueKind ==
                     JsonValueKind.Array)
            {
                foreach (JsonElement item in
                         element.EnumerateArray())
                {
                    QuotaInfo? result =
                        FindQuota(
                            item);

                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        private static string GetString(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement property))
            {
                return string.Empty;
            }

            return property.ValueKind ==
                   JsonValueKind.String
                ? property.GetString() ??
                  string.Empty
                : property.ToString();
        }

        private static long GetInt64(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement property))
            {
                return 0;
            }

            if (property.ValueKind ==
                    JsonValueKind.Number &&
                property.TryGetInt64(
                    out long number))
            {
                return number;
            }

            string value =
                property.ToString()
                    .Replace(
                        ",",
                        string.Empty,
                        StringComparison.Ordinal);

            return long.TryParse(
                value,
                out long parsed)
                ? parsed
                : 0;
        }

        private void UpdateQuotaUi(
            QuotaInfo info)
        {
            PackageNameTextBlock.Text =
                info.Name;

            TotalCharactersTextBlock.Text =
                info.Total.ToString(
                    "N0");

            RemainingCharactersTextBlock.Text =
                info.Remaining.ToString(
                    "N0");

            UsedCharactersTextBlock.Text =
                info.Used.ToString(
                    "N0");

            PackageStatusTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    info.Status)
                    ? "-"
                    : info.Status;

            EffectiveTimeTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    info.EffectiveTime)
                    ? "-"
                    : info.EffectiveTime;

            ExpireTimeTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    info.ExpireTime)
                    ? "-"
                    : info.ExpireTime;

            RemainingPercentTextBlock.Text =
                info.Percent.ToString(
                    "0.##") +
                "%";

            RemainingProgressBar.Value =
                Math.Clamp(
                    info.Percent,
                    0,
                    100);

            RemainingPercentTextBlock.Foreground =
                info.Percent <= 10
                    ? Brushes.Red
                    : info.Percent <= 30
                        ? new SolidColorBrush(
                            Color.FromRgb(
                                202,
                                138,
                                4))
                        : new SolidColorBrush(
                            Color.FromRgb(
                                37,
                                99,
                                235));
        }

        private bool ShowLoginWindow()
        {
            YoudaoLoginWindow loginWindow =
                new YoudaoLoginWindow
                {
                    Owner =
                        this
                };

            return loginWindow.ShowDialog() ==
                   true;
        }

        private static bool IsLoginUrl(
            string url)
        {
            return url.Contains(
                       "login",
                       StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(
                       "passport",
                       StringComparison.OrdinalIgnoreCase) ||
                   url.Contains(
                       "reg.163.com",
                       StringComparison.OrdinalIgnoreCase);
        }

        private void SetBusyState(
            bool busy,
            string message)
        {
            SyncStatusTextBlock.Foreground =
                Brushes.Gray;

            SyncStatusTextBlock.Text =
                message;

            SetButtonsEnabled(
                !busy);
        }

        private void SetButtonsEnabled(
            bool enabled)
        {
            RefreshButton.IsEnabled =
                enabled;

            ReloginButton.IsEnabled =
                enabled;
        }

        private void ShowError(
            string message)
        {
            SyncStatusTextBlock.Foreground =
                Brushes.Red;

            SyncStatusTextBlock.Text =
                message;
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshQuotaAsync(
                allowLoginWindow: true);
        }

        private async void ReloginButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_querying)
            {
                return;
            }

            try
            {
                if (QueryWebView.CoreWebView2 != null)
                {
                    await QueryWebView.CoreWebView2
                        .Profile
                        .ClearBrowsingDataAsync(
                            CoreWebView2BrowsingDataKinds
                                .AllProfile);
                }
            }
            catch
            {
            }

            bool loggedIn =
                ShowLoginWindow();

            if (loggedIn)
            {
                await RefreshQuotaAsync(
                    allowLoginWindow: false);
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void YoudaoQuotaWindow_Closed(
            object? sender,
            EventArgs e)
        {
            QueryWebView.Dispose();
        }

        private sealed class QuotaInfo
        {
            public string Name { get; set; } =
                string.Empty;

            public long Total { get; set; }

            public long Remaining { get; set; }

            public long Used { get; set; }

            public double Percent { get; set; }

            public string Status { get; set; } =
                string.Empty;

            public string EffectiveTime { get; set; } =
                string.Empty;

            public string ExpireTime { get; set; } =
                string.Empty;
        }

        private sealed class QueryResult
        {
            public bool Success { get; private set; }

            public bool RequiresLogin { get; private set; }

            public string Message { get; private set; } =
                string.Empty;

            public QuotaInfo? Quota { get; private set; }

            public static QueryResult Succeeded(
                QuotaInfo quota)
            {
                return new QueryResult
                {
                    Success =
                        true,

                    Quota =
                        quota
                };
            }

            public static QueryResult Failed(
                string message)
            {
                return new QueryResult
                {
                    Message =
                        message
                };
            }

            public static QueryResult LoginRequired(
                string message)
            {
                return new QueryResult
                {
                    RequiresLogin =
                        true,

                    Message =
                        message
                };
            }
        }
    }
}
