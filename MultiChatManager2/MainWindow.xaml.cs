using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using System.Linq;
using System.Text.Json;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class MainWindow : Window
    {
        public static bool TranslateVisibleOnly = true;
        private const string LineExtensionId =
            "ophjlpahpchlmihnnnihgmmeilfjmjjc";

        private const string LinePlatform =
            "LINE";

        private const string WhatsAppPlatform =
            "WhatsApp";

        private const string AccountDragDataFormat =
            "MultiChatManager2.AccountId";

        private readonly Dictionary<string, WebView2>
            _accountViews = new();

        private readonly Dictionary<string, Button>
            _accountButtons = new();
        private readonly Dictionary<string, Border>
    _accountUnreadBadges = new();

        private readonly Dictionary<string, TextBlock>
            _accountUnreadTexts = new();

        private readonly Dictionary<string, AccountLoginStateMonitor>
            _accountLoginStateMonitors = new();

        private readonly List<AccountInfo>
            _accounts = new();

        private Point _accountDragStartPoint;
        private string? _draggedAccountId;
        private Button? _accountDragButton;
        private TranslateTransform? _accountDragTransform;
        private bool _isAccountDragging;

        private readonly string _appFolder;

        private readonly string _dataFolder;

        private readonly string _profilesFolder;

        private readonly string _databasePath;

        private readonly string _extensionPath;

        private readonly string _lineIconPath;
        private readonly YoudaoTranslator _youdaoTranslator;

        private readonly LineTranslationManager
            _lineTranslationManager;

        private readonly WhatsAppTranslationManager
            _whatsAppTranslationManager;
        private bool _isDarkMode;
        private bool _isSavingLineSessions;
        private bool _canCloseMainWindow;
        private readonly string _themeSettingsPath;
        private readonly string _lockSettingsPath;
        private readonly string _translationPresentationSettingsPath;
        private TranslationPresentationSettings
            _translationPresentationSettings = new();
        private bool _isLoadingTranslationPresentationSettings =
            true;

        public MainWindow()
        {
            InitializeComponent();

            AccountPanel.AllowDrop =
                true;

            AccountPanel.Background =
                Brushes.Transparent;

            AccountPanel.PreviewDragOver +=
                AccountPanel_PreviewDragOver;

            AccountPanel.Drop +=
                AccountPanel_Drop;

            _appFolder =
                AppDomain.CurrentDomain.BaseDirectory;

            _dataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "LcyLineTranslate");

            _profilesFolder =
                Path.Combine(
                    _dataFolder,
                    "Profiles");

            _databasePath =
                Path.Combine(
                    _dataFolder,
                    "accounts.db");

            _extensionPath =
                Path.Combine(
                    _appFolder,
                    "Extensions",
                    "LINE");

            _lineIconPath =
                Path.Combine(
                    _appFolder,
                    "Assets",
                    "line.png");

            _themeSettingsPath =
                Path.Combine(
                    _dataFolder,
                    "theme.txt");

            _lockSettingsPath =
                Path.Combine(
                    _dataFolder,
                    "locksettings.json");

            _translationPresentationSettingsPath =
                Path.Combine(
                    _dataFolder,
                    "translation-presentation.json");

            _youdaoTranslator =
                new YoudaoTranslator(
                    _dataFolder);

            _youdaoTranslator
                .LoadConfiguration();

            _lineTranslationManager =
                new LineTranslationManager(
                    _youdaoTranslator,
                    _translationPresentationSettings);

            _whatsAppTranslationManager =
                new WhatsAppTranslationManager(
                    _youdaoTranslator,
                    _translationPresentationSettings);

            Directory.CreateDirectory(
                _dataFolder);

            Directory.CreateDirectory(
                _profilesFolder);

            InitializeDatabase();

            LoadTranslationPresentationSettings();

            LoadThemeSetting();

            Loaded +=
                MainWindow_Loaded;

            ApplyTheme();

            InitializeUpdateModule();

            Closed +=
                MainWindow_Closed;
        }

        private void LicenseStatusButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SettingsPopup.IsOpen =
                false;

            LicenseStatusWindow statusWindow =
                new LicenseStatusWindow
                {
                    Owner =
                        this
                };

            statusWindow.ShowDialog();
        }

        private void LockButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LockSettings? settings =
                LoadLockSettings();

            if (settings == null ||
                string.IsNullOrWhiteSpace(
                    settings.PasswordHash))
            {
                MessageBoxResult result =
                    MessageBox.Show(
                        "尚未设置锁定密码，是否现在设置？",
                        "锁定软件",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                if (result !=
                    MessageBoxResult.Yes)
                {
                    return;
                }

                SetLockPasswordWindow passwordWindow =
                    new SetLockPasswordWindow(
                        _lockSettingsPath)
                    {
                        Owner =
                            this
                    };

                if (passwordWindow.ShowDialog() != true)
                {
                    return;
                }
            }

            SettingsPopup.IsOpen =
                false;

            LockWindow lockWindow =
                new LockWindow(
                    _lockSettingsPath)
                {
                    Owner = this,
                    Width = ActualWidth,
                    Height = ActualHeight,
                    Left = Left,
                    Top = Top
                };

            lockWindow.ShowDialog();

            if (WindowState ==
                WindowState.Minimized)
            {
                WindowState =
                    WindowState.Normal;

                Activate();
            }
        }
        private LockSettings? LoadLockSettings()
        {
            try
            {
                if (!File.Exists(
                        _lockSettingsPath))
                {
                    return null;
                }

                string json =
                    File.ReadAllText(
                        _lockSettingsPath,
                        Encoding.UTF8);

                return JsonSerializer
                    .Deserialize<LockSettings>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });
            }
            catch
            {
                return null;
            }
        }
        private string? ReadSavedLicenseKey()
        {
            try
            {
                string licenseFilePath =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "MultiChatManager2",
                        "license.json");

                if (!File.Exists(
                        licenseFilePath))
                {
                    return null;
                }

                string json =
                    File.ReadAllText(
                        licenseFilePath,
                        Encoding.UTF8);

                using JsonDocument document =
                    JsonDocument.Parse(
                        json);

                if (!document.RootElement
                        .TryGetProperty(
                            "LicenseKey",
                            out JsonElement licenseKeyElement) &&
                    !document.RootElement
                        .TryGetProperty(
                            "licenseKey",
                            out licenseKeyElement))
                {
                    return null;
                }

                return licenseKeyElement
                    .GetString()?
                    .Trim();
            }
            catch
            {
                return null;
            }
        }

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SettingsPopup.IsOpen =
                !SettingsPopup.IsOpen;
        }

        private void LoadTranslationPresentationSettings()
        {
            TranslationPresentationSettings loadedSettings =
                TranslationPresentationSettingsStore.Load(
                    _translationPresentationSettingsPath);

            _translationPresentationSettings.FontSize =
                loadedSettings.FontSize;

            _translationPresentationSettings.DisplayMode =
                loadedSettings.DisplayMode;

            _isLoadingTranslationPresentationSettings =
                true;

            try
            {
                TranslationFontSizeComboBox.SelectedValue =
                    _translationPresentationSettings.FontSize
                        .ToString(
                            "0",
                            CultureInfo.InvariantCulture);

                TranslationDisplayModeComboBox.SelectedValue =
                    _translationPresentationSettings.DisplayMode;
            }
            finally
            {
                _isLoadingTranslationPresentationSettings =
                    false;
            }

            TranslationFontSizeComboBox.SelectionChanged +=
                TranslationPresentationSetting_SelectionChanged;

            TranslationDisplayModeComboBox.SelectionChanged +=
                TranslationPresentationSetting_SelectionChanged;
        }

        private async void TranslationPresentationSetting_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isLoadingTranslationPresentationSettings)
            {
                return;
            }

            if (TranslationFontSizeComboBox is null ||
                TranslationDisplayModeComboBox is null)
            {
                return;
            }

            if (TranslationFontSizeComboBox.SelectedValue is not
                    string fontSizeValue ||
                !double.TryParse(
                    fontSizeValue,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out double fontSize) ||
                TranslationDisplayModeComboBox.SelectedValue is not
                    string displayMode)
            {
                return;
            }

            _translationPresentationSettings.FontSize =
                fontSize;

            _translationPresentationSettings.DisplayMode =
                displayMode;

            TranslationPresentationSettingsStore.Save(
                _translationPresentationSettingsPath,
                _translationPresentationSettings);

            await ApplyTranslationPresentationSettingsAsync();
        }

        private async Task ApplyTranslationPresentationSettingsAsync()
        {
            string settingsJson =
                JsonSerializer.Serialize(
                    _translationPresentationSettings);

            string script =
                "(()=>{window.__mcmUpdateLineTranslationSettings?.(" +
                settingsJson +
                ");window.__mcmUpdateWhatsAppTranslationSettings?.(" +
                settingsJson +
                ");})();";

            foreach (WebView2 webView in
                     _accountViews.Values.ToList())
            {
                if (webView.CoreWebView2 is null)
                {
                    continue;
                }

                try
                {
                    await webView.CoreWebView2
                        .ExecuteScriptAsync(script);
                }
                catch
                {
                    // 页面正在跳转或已关闭时，下一次脚本注入会读取最新设置。
                }
            }
        }

        private void TranslateVisibleChatOnlyCheckBox_Click(
    object sender,
    RoutedEventArgs e)
        {
            TranslateVisibleOnly =
                TranslateVisibleChatOnlyCheckBox.IsChecked == true;
        }
        private void TranslationQuotaButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            YoudaoQuotaWindow window =
                new YoudaoQuotaWindow
                {
                    Owner = this
                };

            window.ShowDialog();
        }
        private void SetLockPasswordButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SettingsPopup.IsOpen =
                false;

            string lockSettingsPath =
                Path.Combine(
                    _dataFolder,
                    "locksettings.json");

            SetLockPasswordWindow passwordWindow =
                new SetLockPasswordWindow(
                    lockSettingsPath)
                {
                    Owner =
                        this
                };

            passwordWindow.ShowDialog();
        }
        private void SetYoudaoKeyButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            SettingsPopup.IsOpen =
                false;

            YoudaoKeyWindow keyWindow =
                new YoudaoKeyWindow(
                    _youdaoTranslator)
                {
                    Owner =
                        this
                };

            keyWindow.ShowDialog();
        }

        private void ClearTranslationCacheButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SettingsPopup.IsOpen =
                false;

            _lineTranslationManager
                .ClearTranslationCache();

            MessageBox.Show(
                this,
                "已清除本次打开软件期间保存的翻译缓存。\n\n" +
                "已显示的译文不会消失；有道 Key、LINE 登录、激活码和聊天记录也不会受影响。",
                "翻译缓存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        private void ThemeToggle_Click(
            object sender,
            RoutedEventArgs e)
        {
            _isDarkMode =
                !_isDarkMode;

            ApplyTheme();

            SaveThemeSetting();

            SettingsPopup.IsOpen =
                false;
        }

        private void LoadThemeSetting()
        {
            try
            {
                if (!File.Exists(
                        _themeSettingsPath))
                {
                    _isDarkMode =
                        false;

                    return;
                }

                string value =
                    File.ReadAllText(
                        _themeSettingsPath)
                        .Trim();

                _isDarkMode =
                    string.Equals(
                        value,
                        "dark",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                _isDarkMode =
                    false;
            }
        }

        private void SaveThemeSetting()
        {
            try
            {
                File.WriteAllText(
                    _themeSettingsPath,
                    _isDarkMode
                        ? "dark"
                        : "light");
            }
            catch
            {
            }
        }

        private void ApplyTheme()
        {
            if (_isDarkMode)
            {
                SetBrushColor(
                    "WindowBackgroundBrush",
                    "#292929");

                SetBrushColor(
                    "BrowserBackgroundBrush",
                    "#292929");

                SetBrushColor(
                    "SidebarBackgroundBrush",
                    "#222426");

                SetBrushColor(
                    "TopBarBackgroundBrush",
                    "#292929");

                SetBrushColor(
                    "MainTextBrush",
                    "#F3F4F6");

                SetBrushColor(
                    "SecondaryTextBrush",
                    "#B6BBC2");

                SetBrushColor(
                    "DividerBrush",
                    "#404246");

                SetBrushColor(
                    "ButtonBackgroundBrush",
                    "#303236");

                SetBrushColor(
                    "ButtonBorderBrush",
                    "#4B4E53");

                SetBrushColor(
                    "ButtonHoverBrush",
                    "#3D4045");

                SetBrushColor(
                    "ButtonPressedBrush",
                    "#4A4D52");

                SetBrushColor(
                    "ThemeButtonBackgroundBrush",
                    "#2E3033");

                SetBrushColor(
                    "ThemeButtonHoverBrush",
                    "#3B3E43");

                if (ThemeToggleButton != null)
                {
                    ThemeToggleButton.Content =
                        "☀ 日间模式";
                }
            }
            else
            {
                SetBrushColor(
                    "WindowBackgroundBrush",
                    "#FFFFFF");

                SetBrushColor(
                    "BrowserBackgroundBrush",
                    "#FFFFFF");

                SetBrushColor(
                    "SidebarBackgroundBrush",
                    "#EEEEEE");

                SetBrushColor(
                    "TopBarBackgroundBrush",
                    "#F7F7F7");

                SetBrushColor(
                    "MainTextBrush",
                    "#222222");

                SetBrushColor(
                    "SecondaryTextBrush",
                    "#777777");

                SetBrushColor(
                    "DividerBrush",
                    "#DDDDDD");

                SetBrushColor(
                    "ButtonBackgroundBrush",
                    "#FFFFFF");

                SetBrushColor(
                    "ButtonBorderBrush",
                    "#C8C8C8");

                SetBrushColor(
                    "ButtonHoverBrush",
                    "#E1E1E1");

                SetBrushColor(
                    "ButtonPressedBrush",
                    "#D2D2D2");

                SetBrushColor(
                    "ThemeButtonBackgroundBrush",
                    "#FFFFFF");

                SetBrushColor(
                    "ThemeButtonHoverBrush",
                    "#ECECEC");

                if (ThemeToggleButton != null)
                {
                    ThemeToggleButton.Content =
                        "🌙 夜间模式";
                }
            }

            UpdateAccountTextColors();

            ApplyThemeToAllWebViews();
        }
        private void ApplyThemeToAllWebViews()
        {
            foreach (WebView2 webView
                     in _accountViews.Values)
            {
                ApplyThemeToWebView(
                    webView);
            }
        }

        private async void ApplyThemeToWebView(
            WebView2 webView)
        {
            if (webView.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                string script;

                if (_isDarkMode)
                {
                    script =
                    """
                    (() => {
                        let style =
                            document.getElementById(
                                "multi-chat-dark-theme");

                        if (!style) {
                            style =
                                document.createElement(
                                    "style");

                            style.id =
                                "multi-chat-dark-theme";

                            document.head.appendChild(
                                style);
                        }

                        style.textContent = `
                            html {
                                /*
                                 * 先把网页基底设为白色，再完整反色为炭黑。
                                 * 原方案先设深色再只反色 88%，会把基底反成浅灰。
                                 */
                                background: #ffffff !important;
                                color-scheme: dark !important;
                                filter: invert(100%) hue-rotate(180deg) !important;
                            }

                            body {
                                background: #ffffff !important;
                            }

                            img,
                            picture,
                            video,
                            canvas,
                            svg,
                            iframe {
                                filter: invert(100%) hue-rotate(180deg) !important;
                            }

                            input,
                            textarea,
                            select,
                            button {
                                color: inherit !important;
                            }

                            input::placeholder,
                            textarea::placeholder {
                                color: #777777 !important;
                                opacity: 1 !important;
                            }
                        `;

                        document.documentElement.style.backgroundColor =
                            "#ffffff";

                        document.body.style.backgroundColor =
                            "#ffffff";
                    })();
                    """;
                }
                else
                {
                    script =
                    """
                    (() => {
                        const style =
                            document.getElementById(
                                "multi-chat-dark-theme");

                        if (style) {
                            style.remove();
                        }

                        document.documentElement.style.filter =
                            "";

                        document.documentElement.style.backgroundColor =
                            "";

                        if (document.body) {
                            document.body.style.backgroundColor =
                                "";
                        }
                    })();
                    """;
                }

                await webView.CoreWebView2
                    .ExecuteScriptAsync(
                        script);
            }
            catch
            {
            }
        }
        private void SetBrushColor(
            string resourceKey,
            string colorValue)
        {
            Color color =
                (Color)ColorConverter.ConvertFromString(
                    colorValue);

            Resources[resourceKey] =
                new SolidColorBrush(
                    color);
        }

        private void UpdateAccountTextColors()
        {
            Brush textBrush =
                (Brush)FindResource(
                    "MainTextBrush");

            foreach (Button button
                     in _accountButtons.Values)
            {
                UpdateTextColorRecursive(
                    button.Content,
                    textBrush);
            }
        }

        private void UpdateTextColorRecursive(
            object? element,
            Brush brush)
        {
            if (element is TextBlock textBlock)
            {
                if (textBlock.Foreground ==
                    Brushes.White)
                {
                    return;
                }

                textBlock.Foreground =
                    brush;

                return;
            }

            if (element is Panel panel)
            {
                foreach (UIElement child
                         in panel.Children)
                {
                    UpdateTextColorRecursive(
                        child,
                        brush);
                }

                return;
            }

            if (element is Border border &&
                border.Child != null)
            {
                UpdateTextColorRecursive(
                    border.Child,
                    brush);
            }
        }
        private string GetConnectionString()
        {
            return
                $"Data Source={_databasePath}";
        }

        private void InitializeDatabase()
        {
            using SqliteConnection connection =
                new SqliteConnection(
                    GetConnectionString());

            connection.Open();

            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
        CREATE TABLE IF NOT EXISTS Accounts
        (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            SortOrder INTEGER NOT NULL,
            Platform TEXT NOT NULL DEFAULT 'LINE'
        );
        """;

        command.ExecuteNonQuery();

        EnsureAccountsPlatformColumn(
            connection);
    }

    private static void EnsureAccountsPlatformColumn(
        SqliteConnection connection)
    {
        bool hasPlatformColumn =
            false;

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA table_info(Accounts);";

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                if (string.Equals(
                        reader.GetString(1),
                        "Platform",
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasPlatformColumn =
                        true;

                    break;
                }
            }
        }

        if (hasPlatformColumn)
        {
            return;
        }

        using SqliteCommand migrationCommand =
            connection.CreateCommand();

        migrationCommand.CommandText =
        """
        ALTER TABLE Accounts
        ADD COLUMN Platform TEXT NOT NULL DEFAULT 'LINE';
        """;

        migrationCommand.ExecuteNonQuery();
    }

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            EmptyMessage.Visibility =
                Visibility.Collapsed;

            LoadAccounts();

            if (_accounts.Count == 0)
            {
                return;
            }

            btnAdd.IsEnabled =
                false;

            try
            {
                foreach (AccountInfo account in _accounts)
                {
                    await CreateAccountView(
                        account);
                }

                ShowAccount(
                    _accounts[0].Id);
            }
            finally
            {
                btnAdd.IsEnabled =
                    true;
            }
        }
        private async void MainWindow_Closing(
    object? sender,
    System.ComponentModel.CancelEventArgs e)
        {
            if (_canCloseMainWindow)
            {
                return;
            }

            e.Cancel = true;

            if (_isSavingLineSessions)
            {
                return;
            }

            _isSavingLineSessions = true;

            try
            {
                foreach (AccountLoginStateMonitor monitor in
                         _accountLoginStateMonitors.Values.ToArray())
                {
                    monitor.Dispose();
                }

                _accountLoginStateMonitors.Clear();

                foreach (WebView2 webView in
                         _accountViews.Values.ToArray())
                {
                    try
                    {
                        webView.Visibility = Visibility.Collapsed;
                        webView.Dispose();
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(500);
            }
            finally
            {
                _canCloseMainWindow = true;
                Close();
            }
        }
        private void MainWindow_Closed(
    object? sender,
    EventArgs e)
        {
            DisposeUpdateModule();
        }


        private async void BtnAdd_Click(
            object sender,
            RoutedEventArgs e)
        {
            AccountPlatformSelectionWindow selectionWindow =
                new AccountPlatformSelectionWindow
                {
                    Owner = this
                };

            if (selectionWindow.ShowDialog() != true ||
                string.IsNullOrWhiteSpace(
                    selectionWindow.SelectedPlatform))
            {
                return;
            }

            btnAdd.IsEnabled =
                false;

            AccountInfo account =
                new AccountInfo
                {
                    Id =
                        Guid.NewGuid()
                            .ToString("N"),

                    Name =
                        GetNextAccountName(
                            selectionWindow.SelectedPlatform),

                    Platform =
                        selectionWindow.SelectedPlatform,

                    SortOrder =
                        GetNextSortOrder()
                };

            try
            {
                _accounts.Add(
                    account);

                InsertAccount(
                    account);

                await CreateAccountView(
                    account);

                ShowAccount(
                    account.Id);
            }
            catch (Exception ex)
            {
                _accounts.Remove(
                    account);

                DeleteAccountFromDatabase(
                    account.Id);

                MessageBox.Show(
                    ex.Message,
                    "添加账号失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                btnAdd.IsEnabled =
                    true;
            }
        }

        private string GetNextAccountName(
            string platform)
        {
            int number = 1;

            string prefix =
                IsWhatsAppPlatform(platform)
                    ? "WhatsApp"
                    : "账号";

            while (_accounts.Any(
                account =>
                    string.Equals(
                        account.Platform,
                        platform,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        account.Name,
                        $"{prefix} {number}",
                        StringComparison.OrdinalIgnoreCase)))
            {
                number++;
            }

            return
                $"{prefix} {number}";
        }

        private int GetNextSortOrder()
        {
            if (_accounts.Count == 0)
            {
                return 1;
            }

            return
                _accounts.Max(
                    account =>
                        account.SortOrder) + 1;
        }

        private async Task CreateAccountView(
            AccountInfo account)
        {
            if (_accountViews.ContainsKey(
                account.Id))
            {
                return;
            }

            if (IsWhatsAppPlatform(
                    account.Platform))
            {
                await CreateWhatsAppAccountView(
                    account);

                return;
            }

            ValidateRequiredFiles();

            string profileFolder =
                Path.Combine(
                    _profilesFolder,
                    account.Id);

            Directory.CreateDirectory(
                profileFolder);

            WebView2 webView =
                new WebView2
                {
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,

                    VerticalAlignment =
                        VerticalAlignment.Stretch,

                    Visibility =
                        Visibility.Collapsed,

                    CreationProperties =
                        new CoreWebView2CreationProperties
                        {
                            UserDataFolder =
                                profileFolder,

                            AreBrowserExtensionsEnabled =
                                true
                        }
                };

            BrowserContainer.Children.Add(
                webView);

            try
            {
                await Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading
                        .DispatcherPriority.Loaded);

                await webView
                    .EnsureCoreWebView2Async();

                await SetupUnreadMonitoring(
                    webView,
                    account.Id);

                await _lineTranslationManager
                    .AttachAsync(
                        webView,
                        account.Id);

                await LoadAndOpenLineExtension(
                    webView);
                webView.CoreWebView2.NavigationCompleted +=
    (_, __) =>
    {
        ApplyThemeToWebView(
            webView);
    };
                _accountViews.Add(
                    account.Id,
                    webView);

                CreateAccountButton(
                    account);

                StartAccountLoginStateMonitoring(
                    account,
                    webView);
            }
            catch
            {
                BrowserContainer.Children.Remove(
                    webView);

                webView.Dispose();

                throw;
            }
        }

        private async Task CreateWhatsAppAccountView(
            AccountInfo account)
        {
            string profileFolder =
                Path.Combine(
                    _profilesFolder,
                    account.Id);

            Directory.CreateDirectory(
                profileFolder);

            WebView2 webView =
                new WebView2
                {
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,

                    VerticalAlignment =
                        VerticalAlignment.Stretch,

                    Visibility =
                        Visibility.Collapsed,

                    CreationProperties =
                        new CoreWebView2CreationProperties
                        {
                            UserDataFolder =
                                profileFolder,

                            AreBrowserExtensionsEnabled =
                                false
                        }
                };

            BrowserContainer.Children.Add(
                webView);

            try
            {
                await Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading
                        .DispatcherPriority.Loaded);

                await webView
                    .EnsureCoreWebView2Async();

                await _whatsAppTranslationManager
                    .AttachAsync(
                        webView,
                        account.Id);

                webView.CoreWebView2.NavigationCompleted +=
                    (_, __) =>
                    {
                        ApplyThemeToWebView(
                            webView);
                    };

                _accountViews.Add(
                    account.Id,
                    webView);

                CreateAccountButton(
                    account);

                StartAccountLoginStateMonitoring(
                    account,
                    webView);

                webView.CoreWebView2.Navigate(
                    "https://web.whatsapp.com/");
            }
            catch
            {
                BrowserContainer.Children.Remove(
                    webView);

                webView.Dispose();

                throw;
            }
        }

        private static bool IsWhatsAppPlatform(
            string? platform)
        {
            return string.Equals(
                platform,
                WhatsAppPlatform,
                StringComparison.OrdinalIgnoreCase);
        }

        private void StartAccountLoginStateMonitoring(
            AccountInfo account,
            WebView2 webView)
        {
            if (!_accountButtons.TryGetValue(
                    account.Id,
                    out Button? button))
            {
                return;
            }

            if (_accountLoginStateMonitors.Remove(
                    account.Id,
                    out AccountLoginStateMonitor? existingMonitor))
            {
                existingMonitor.Dispose();
            }

            AccountLoginStateMonitor monitor =
                new AccountLoginStateMonitor(
                    webView,
                    button,
                    account.Platform);

            _accountLoginStateMonitors.Add(
                account.Id,
                monitor);

            monitor.Start();
        }

        private void ValidateRequiredFiles()
        {
            if (!Directory.Exists(_extensionPath))
            {
                throw new DirectoryNotFoundException(
                    "未找到 Extensions\\LINE 文件夹。");
            }

            if (!File.Exists(
                Path.Combine(
                    _extensionPath,
                    "manifest.json")))
            {
                throw new FileNotFoundException(
                    "Extensions\\LINE 中缺少 manifest.json。");
            }

            if (!File.Exists(_lineIconPath))
            {
                throw new FileNotFoundException(
                    "Assets\\line.png 不存在。");
            }
        }
        private async Task SetupUnreadMonitoring(
    WebView2 webView,
    string accountId)
        {
            webView.CoreWebView2.WebMessageReceived +=
                (_, messageArgs) =>
                {
                    try
                    {
                        string message =
                            messageArgs.TryGetWebMessageAsString();

                        using JsonDocument document =
                            JsonDocument.Parse(
                                message);

                        JsonElement root =
                            document.RootElement;

                        if (!root.TryGetProperty(
                                "type",
                                out JsonElement typeElement))
                        {
                            return;
                        }

                        if (typeElement.GetString() !=
                            "lineUnread")
                        {
                            return;
                        }

                        if (!root.TryGetProperty(
                                "count",
                                out JsonElement countElement))
                        {
                            return;
                        }

                        int unreadCount =
                            countElement.GetInt32();

                        Dispatcher.Invoke(
                            () =>
                            {
                                UpdateUnreadBadge(
                                    accountId,
                                    unreadCount);
                            });
                    }
                    catch
                    {
                    }
                };

            string unreadScript =
            """
            (() => {
                if (window.__multiChatUnreadInstalled) {
                    return;
                }

                window.__multiChatUnreadInstalled = true;

                let lastUnreadCount = -1;
                let timer = null;

                function isVisible(element) {
                    if (!element) {
                        return false;
                    }

                    const style =
                        window.getComputedStyle(element);

                    if (style.display === "none" ||
                        style.visibility === "hidden" ||
                        Number(style.opacity) === 0) {
                        return false;
                    }

                    const rect =
                        element.getBoundingClientRect();

                    return rect.width > 0 &&
                           rect.height > 0;
                }

                function readNumber(text) {
                    if (!text) {
                        return 0;
                    }

                    const cleaned =
                        String(text)
                            .trim()
                            .replace(/\s+/g, "");

                    if (/^\d+$/.test(cleaned)) {
                        return Number(cleaned);
                    }

                    if (/^\d+\+$/.test(cleaned)) {
                        return Number(
                            cleaned.replace("+", ""));
                    }

                    return 0;
                }

                function readTitleCount() {
                    const match =
                        document.title.match(
                            /^\s*\((\d+)\)/);

                    if (!match) {
                        return 0;
                    }

                    return Number(match[1]);
                }

                function looksLikeUnreadBadge(element) {
                    if (!isVisible(element)) {
                        return false;
                    }

                    const text =
                        element.textContent?.trim() ?? "";

                    const count =
                        readNumber(text);

                    if (count <= 0) {
                        return false;
                    }

                    const rect =
                        element.getBoundingClientRect();

                    if (rect.width > 55 ||
                        rect.height > 40) {
                        return false;
                    }

                    const style =
                        window.getComputedStyle(element);

                    const markerText =
                        (
                            element.className?.toString() +
                            " " +
                            element.getAttribute("aria-label") +
                            " " +
                            element.getAttribute("title") +
                            " " +
                            element.getAttribute("data-testid")
                        ).toLowerCase();

                    if (markerText.includes("unread") ||
                        markerText.includes("badge") ||
                        markerText.includes("notification")) {
                        return true;
                    }

                    const colorMatch =
                        style.backgroundColor.match(
                            /rgba?\((\d+),\s*(\d+),\s*(\d+)/);

                    if (!colorMatch) {
                        return false;
                    }

                    const red =
                        Number(colorMatch[1]);

                    const green =
                        Number(colorMatch[2]);

                    const blue =
                        Number(colorMatch[3]);

                    return red > 150 &&
                           red > green * 1.25 &&
                           red > blue * 1.25;
                }

                function calculateUnreadCount() {
                    const titleCount =
                        readTitleCount();

                    if (titleCount > 0) {
                        return titleCount;
                    }

                    let total = 0;

                    const candidates =
                        document.querySelectorAll(
                            [
                                '[class*="unread" i]',
                                '[class*="badge" i]',
                                '[class*="notification" i]',
                                '[aria-label*="unread" i]',
                                '[title*="unread" i]',
                                '[data-testid*="unread" i]',
                                'span',
                                'div'
                            ].join(",")
                        );

                    const used =
                        new Set();

                    for (const element of candidates) {
                        if (used.has(element)) {
                            continue;
                        }

                        used.add(element);

                        if (!looksLikeUnreadBadge(element)) {
                            continue;
                        }

                        total +=
                            readNumber(
                                element.textContent);
                    }

                    return total;
                }

                function sendUnreadCount() {
                    const unreadCount =
                        Math.max(
                            0,
                            calculateUnreadCount());

                    if (unreadCount ===
                        lastUnreadCount) {
                        return;
                    }

                    lastUnreadCount =
                        unreadCount;

                    window.chrome.webview.postMessage(
                        JSON.stringify({
                            type: "lineUnread",
                            count: unreadCount
                        })
                    );
                }

                function scheduleUpdate() {
                    clearTimeout(timer);

                    timer =
                        setTimeout(
                            sendUnreadCount,
                            250);
                }

                const observer =
                    new MutationObserver(
                        scheduleUpdate);

                function startObserver() {
                    if (!document.documentElement) {
                        setTimeout(
                            startObserver,
                            100);

                        return;
                    }

                    observer.observe(
                        document.documentElement,
                        {
                            childList: true,
                            subtree: true,
                            attributes: true,
                            characterData: true
                        });

                    sendUnreadCount();

                    setInterval(
                        sendUnreadCount,
                        2000);
                }

                startObserver();
            })();
            """;

            await webView.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(
                    unreadScript);
        }

        private void ResetUnreadBadge(
            string accountId)
        {
            UpdateUnreadBadge(
                accountId,
                0);
        }
        private async Task LoadAndOpenLineExtension(
            WebView2 webView)
        {
            CoreWebView2Profile profile =
                webView.CoreWebView2.Profile;

            IReadOnlyList<CoreWebView2BrowserExtension>
                extensions =
                    await profile
                        .GetBrowserExtensionsAsync();

            CoreWebView2BrowserExtension? lineExtension =
                extensions.FirstOrDefault(
                    extension =>
                        string.Equals(
                            extension.Id,
                            LineExtensionId,
                            StringComparison.OrdinalIgnoreCase));

            if (lineExtension == null)
            {
                lineExtension =
                    await profile
                        .AddBrowserExtensionAsync(
                            _extensionPath);
            }

            string extensionUrl =
                $"chrome-extension://{lineExtension.Id}/index.html";

            webView.CoreWebView2.Navigate(
                extensionUrl);
        }
        private void CreateAccountButton(
            AccountInfo account)
        {
            Button button =
                new Button
                {
                    Width = 68,
                    Height = 86,
                    Margin =
                        new Thickness(
                            0,
                            5,
                            0,
                            5),

                    Style =
                        (Style)FindResource(
                            "AccountItemButtonStyle"),

                    Tag =
                        account.Id,

                    Uid =
                        account.Platform,

                    ToolTip =
                        "拖动可调整账号顺序；右键可上移或下移"
                };

            Grid rootGrid =
                new Grid();

            StackPanel panel =
                new StackPanel
                {
                    HorizontalAlignment =
                        HorizontalAlignment.Center
                };

            Grid iconGrid =
                new Grid
                {
                    Width = 54,
                    Height = 52,
                    HorizontalAlignment =
                        HorizontalAlignment.Center
                };

            if (IsWhatsAppPlatform(
                    account.Platform))
            {
                Border whatsAppIcon =
                    new Border
                    {
                        Tag =
                            "WhatsAppIcon",
                        Width = 48,
                        Height = 48,
                        CornerRadius =
                            new CornerRadius(24),
                        Background =
                            new SolidColorBrush(
                                Color.FromRgb(
                                    112,
                                    112,
                                    112)),
                        Child =
                            new TextBlock
                            {
                                Text = "WA",
                                Foreground =
                                    Brushes.White,
                                FontSize = 13,
                                FontWeight =
                                    FontWeights.Bold,
                                HorizontalAlignment =
                                    HorizontalAlignment.Center,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            }
                    };

                iconGrid.Children.Add(
                    whatsAppIcon);
            }
            else
            {
                Border lineIcon =
                    new Border
                    {
                        Tag =
                            "LineIcon",
                        Width = 48,
                        Height = 48,
                        CornerRadius =
                            new CornerRadius(24),
                        Background =
                            new SolidColorBrush(
                                Color.FromRgb(
                                    112,
                                    112,
                                    112)),
                        Child =
                            new TextBlock
                            {
                                Text = "LINE",
                                Foreground =
                                    Brushes.White,
                                FontSize = 10,
                                FontWeight =
                                    FontWeights.Bold,
                                HorizontalAlignment =
                                    HorizontalAlignment.Center,
                                VerticalAlignment =
                                    VerticalAlignment.Center,
                                TextAlignment =
                                    TextAlignment.Center
                            }
                    };

                iconGrid.Children.Add(
                    lineIcon);
            }

            TextBlock badgeText =
                new TextBlock
                {
                    Text = "0",
                    Foreground =
                        Brushes.White,

                    FontSize = 10,
                    FontWeight =
                        FontWeights.Bold,

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    TextAlignment =
                        TextAlignment.Center
                };

            Border badge =
                new Border
                {
                    MinWidth = 20,
                    Height = 20,
                    Padding =
                        new Thickness(
                            4,
                            0,
                            4,
                            0),

                    CornerRadius =
                        new CornerRadius(10),

                    Background =
                        Brushes.Red,

                    BorderBrush =
                        Brushes.White,

                    BorderThickness =
                        new Thickness(2),

                    HorizontalAlignment =
                        HorizontalAlignment.Right,

                    VerticalAlignment =
                        VerticalAlignment.Top,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            -3,
                            0),

                    Visibility =
                        Visibility.Collapsed,

                    Child =
                        badgeText
                };

            iconGrid.Children.Add(
                badge);

            TextBlock nameText =
                new TextBlock
                {
                    Text =
                        account.Name,

                    FontSize = 10,

                    Foreground =
            (Brush)FindResource(
                "MainTextBrush"),

                    TextAlignment =
                        TextAlignment.Center,

                    TextWrapping =
                        TextWrapping.Wrap
                };

            panel.Children.Add(
                iconGrid);

            panel.Children.Add(
                nameText);

            rootGrid.Children.Add(
                panel);

            button.Content =
                rootGrid;

            button.Click +=
                AccountButton_Click;

            button.PreviewMouseLeftButtonDown +=
                AccountButton_PreviewMouseLeftButtonDown;

            button.PreviewMouseMove +=
                AccountButton_PreviewMouseMove;

            button.PreviewMouseLeftButtonUp +=
                AccountButton_PreviewMouseLeftButtonUp;

            button.LostMouseCapture +=
                AccountButton_LostMouseCapture;

            ContextMenu menu =
                new ContextMenu();

            MenuItem moveUp =
                new MenuItem
                {
                    Header =
                        "上移",

                    Tag =
                        account.Id
                };

            moveUp.Click +=
                MoveAccountUp_Click;

            MenuItem moveDown =
                new MenuItem
                {
                    Header =
                        "下移",

                    Tag =
                        account.Id
                };

            moveDown.Click +=
                MoveAccountDown_Click;

            MenuItem rename =
                new MenuItem
                {
                    Header =
                        "修改名称",

                    Tag =
                        account.Id
                };

            rename.Click +=
                RenameAccount_Click;

            MenuItem delete =
                new MenuItem
                {
                    Header =
                        "删除账号",

                    Tag =
                        account.Id
                };

            delete.Click +=
                DeleteAccount_Click;

            MenuItem refresh =
                new MenuItem
                {
                    Header =
                        IsWhatsAppPlatform(
                            account.Platform)
                            ? "刷新 WhatsApp"
                            : "刷新 LINE",

                    Tag =
                        account.Id
                };

            refresh.Click +=
                RefreshAccount_Click;

            MenuItem clearCache =
                new MenuItem
                {
                    Header =
                        IsWhatsAppPlatform(
                            account.Platform)
                            ? "清理缓存并刷新 WhatsApp"
                            : "清理缓存并刷新 LINE",

                    Tag =
                        account.Id
                };

            clearCache.Click +=
                ClearCache_Click;

            menu.Items.Add(
                moveUp);

            menu.Items.Add(
                moveDown);

            menu.Items.Add(
                new Separator());

            menu.Items.Add(
                rename);

            menu.Items.Add(
                delete);

            menu.Items.Add(
                new Separator());

            menu.Items.Add(
                refresh);

            menu.Items.Add(
                clearCache);

            button.ContextMenu =
                menu;

            _accountButtons.Add(
                account.Id,
                button);

            _accountUnreadBadges.Add(
                account.Id,
                badge);

            _accountUnreadTexts.Add(
                account.Id,
                badgeText);

            AccountPanel.Children.Add(
                button);
        }

        private void AccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is string accountId)
            {
                ShowAccount(
                    accountId);
            }
        }

        private void AccountButton_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string accountId)
            {
                return;
            }

            _draggedAccountId =
                accountId;

            _accountDragStartPoint =
                e.GetPosition(
                    AccountPanel);
        }

        private void AccountButton_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string accountId ||
                _draggedAccountId != accountId)
            {
                return;
            }

            Point currentPoint =
                e.GetPosition(
                    AccountPanel);

            if (_isAccountDragging)
            {
                UpdateAccountDragPosition(
                    currentPoint);

                e.Handled =
                    true;

                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (Math.Abs(
                    currentPoint.X - _accountDragStartPoint.X) <
                    SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(
                    currentPoint.Y - _accountDragStartPoint.Y) <
                    SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            BeginAccountDrag(
                button);

            UpdateAccountDragPosition(
                currentPoint);

            e.Handled =
                true;
        }

        private void AccountButton_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not Button button ||
                _accountDragButton != button)
            {
                _draggedAccountId =
                    null;

                return;
            }

            Point dropPoint =
                e.GetPosition(
                    AccountPanel);

            FinishAccountDrag(
                button,
                dropPoint,
                true);

            e.Handled =
                true;
        }

        private void AccountButton_LostMouseCapture(
            object sender,
            MouseEventArgs e)
        {
            if (sender is Button button &&
                _accountDragButton == button)
            {
                FinishAccountDrag(
                    button,
                    default,
                    false);
            }
        }

        private void BeginAccountDrag(
            Button button)
        {
            _isAccountDragging =
                true;

            _accountDragButton =
                button;

            _accountDragTransform =
                new TranslateTransform();

            button.RenderTransform =
                _accountDragTransform;

            button.Opacity =
                0.92;

            Panel.SetZIndex(
                button,
                1000);

            button.CaptureMouse();
        }

        private void UpdateAccountDragPosition(
            Point currentPoint)
        {
            if (_accountDragTransform is null)
            {
                return;
            }

            _accountDragTransform.X =
                currentPoint.X - _accountDragStartPoint.X;

            _accountDragTransform.Y =
                currentPoint.Y - _accountDragStartPoint.Y;
        }

        private void FinishAccountDrag(
            Button button,
            Point dropPoint,
            bool saveNewOrder)
        {
            bool wasDragging =
                _isAccountDragging;

            _isAccountDragging =
                false;

            _accountDragButton =
                null;

            _accountDragTransform =
                null;

            _draggedAccountId =
                null;

            if (button.IsMouseCaptured)
            {
                button.ReleaseMouseCapture();
            }

            button.ClearValue(
                UIElement.RenderTransformProperty);

            button.Opacity =
                1;

            Panel.SetZIndex(
                button,
                0);

            if (!saveNewOrder || !wasDragging ||
                button.Tag is not string accountId)
            {
                return;
            }

            AccountInfo? source =
                _accounts.FirstOrDefault(
                    account =>
                        account.Id == accountId);

            if (source is null)
            {
                return;
            }

            MoveAccountToIndex(
                source,
                GetAccountDropIndex(
                    source,
                    dropPoint));
        }

        private int GetAccountDropIndex(
            AccountInfo source,
            Point dropPoint)
        {
            int targetIndex =
                _accounts.Count;

            foreach (AccountInfo account in _accounts)
            {
                if (account.Id == source.Id ||
                    !_accountButtons.TryGetValue(
                        account.Id,
                        out Button? candidate))
                {
                    continue;
                }

                Point candidateTopLeft =
                    candidate.TranslatePoint(
                        new Point(),
                        AccountPanel);

                if (dropPoint.Y <
                    candidateTopLeft.Y +
                    candidate.ActualHeight / 2)
                {
                    targetIndex =
                        _accounts.IndexOf(
                            account);

                    break;
                }
            }

            int sourceIndex =
                _accounts.IndexOf(
                    source);

            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            return Math.Clamp(
                targetIndex,
                0,
                _accounts.Count - 1);
        }

        private void AccountPanel_PreviewDragOver(
            object sender,
            DragEventArgs e)
        {
            e.Effects =
                e.Data.GetDataPresent(
                    AccountDragDataFormat)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;

            e.Handled =
                true;
        }

        private void AccountPanel_Drop(
            object sender,
            DragEventArgs e)
        {
            if (e.Data.GetData(
                    AccountDragDataFormat) is not string accountId)
            {
                return;
            }

            AccountInfo? source =
                _accounts.FirstOrDefault(
                    account =>
                        account.Id == accountId);

            if (source is null)
            {
                return;
            }

            Button? targetButton =
                FindAccountButton(
                    e.OriginalSource as DependencyObject);

            if (targetButton?.Tag is string targetId &&
                targetId == accountId)
            {
                return;
            }

            int sourceIndex =
                _accounts.IndexOf(
                    source);

            int targetIndex;

            if (targetButton?.Tag is string targetAccountId)
            {
                AccountInfo? target =
                    _accounts.FirstOrDefault(
                        account =>
                            account.Id == targetAccountId);

                if (target is null)
                {
                    return;
                }

                targetIndex =
                    _accounts.IndexOf(
                        target);

                bool placeAfterTarget =
                    e.GetPosition(
                        targetButton).Y >
                    targetButton.ActualHeight / 2;

                if (placeAfterTarget)
                {
                    targetIndex++;
                }
            }
            else
            {
                targetIndex =
                    _accounts.Count;
            }

            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            MoveAccountToIndex(
                source,
                targetIndex);

            e.Handled =
                true;
        }

        private static Button? FindAccountButton(
            DependencyObject? element)
        {
            while (element is not null)
            {
                if (element is Button button &&
                    button.Tag is string)
                {
                    return button;
                }

                element =
                    VisualTreeHelper.GetParent(
                        element);
            }

            return null;
        }

        private void MoveAccountUp_Click(
            object sender,
            RoutedEventArgs e)
        {
            MoveAccountFromMenu(
                sender,
                -1);
        }

        private void MoveAccountDown_Click(
            object sender,
            RoutedEventArgs e)
        {
            MoveAccountFromMenu(
                sender,
                1);
        }

        private void MoveAccountFromMenu(
            object sender,
            int offset)
        {
            if (sender is not MenuItem menu ||
                menu.Tag is not string accountId)
            {
                return;
            }

            AccountInfo? account =
                _accounts.FirstOrDefault(
                    item =>
                        item.Id == accountId);

            if (account is null)
            {
                return;
            }

            int currentIndex =
                _accounts.IndexOf(
                    account);

            int destinationIndex =
                Math.Clamp(
                    currentIndex + offset,
                    0,
                    _accounts.Count - 1);

            MoveAccountToIndex(
                account,
                destinationIndex);
        }

        private void MoveAccountToIndex(
            AccountInfo account,
            int destinationIndex)
        {
            int sourceIndex =
                _accounts.IndexOf(
                    account);

            if (sourceIndex < 0)
            {
                return;
            }

            destinationIndex =
                Math.Clamp(
                    destinationIndex,
                    0,
                    _accounts.Count - 1);

            if (sourceIndex == destinationIndex)
            {
                return;
            }

            List<AccountInfo> previousOrder =
                _accounts.ToList();

            Dictionary<string, int> previousSortOrders =
                _accounts.ToDictionary(
                    item => item.Id,
                    item => item.SortOrder);

            _accounts.RemoveAt(
                sourceIndex);

            _accounts.Insert(
                destinationIndex,
                account);

            for (int index = 0;
                 index < _accounts.Count;
                 index++)
            {
                _accounts[index].SortOrder =
                    index + 1;
            }

            try
            {
                SaveAccountOrder();
                RebuildAccountButtonOrder();
            }
            catch (Exception exception)
            {
                _accounts.Clear();
                _accounts.AddRange(
                    previousOrder);

                foreach (AccountInfo item in _accounts)
                {
                    item.SortOrder =
                        previousSortOrders[item.Id];
                }

                RebuildAccountButtonOrder();

                MessageBox.Show(
                    this,
                    "保存账号排序失败：\n\n" +
                    exception.Message,
                    "账号排序",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RebuildAccountButtonOrder()
        {
            AccountPanel.Children.Clear();

            foreach (AccountInfo account in _accounts)
            {
                if (_accountButtons.TryGetValue(
                        account.Id,
                        out Button? button))
                {
                    AccountPanel.Children.Add(
                        button);
                }
            }
        }

        private void SaveAccountOrder()
        {
            using SqliteConnection connection =
                new SqliteConnection(
                    GetConnectionString());

            connection.Open();

            using SqliteTransaction transaction =
                connection.BeginTransaction();

            using SqliteCommand command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText =
            """
            UPDATE Accounts
            SET SortOrder = $sort
            WHERE Id = $id;
            """;

            foreach (AccountInfo account in _accounts)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue(
                    "$sort",
                    account.SortOrder);
                command.Parameters.AddWithValue(
                    "$id",
                    account.Id);

                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private void ShowAccount(
            string accountId)
        {
            EmptyMessage.Visibility =
                Visibility.Collapsed;

            foreach (
                KeyValuePair<string, WebView2> item
                in _accountViews)
            {
                item.Value.Visibility =
                    item.Key == accountId
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            foreach (
                KeyValuePair<string, Button> item
                in _accountButtons)
            {
                bool isSelected =
                    item.Key == accountId;

                item.Value.BorderBrush =
                    isSelected
                        ? Brushes.DeepSkyBlue
                        : Brushes.Transparent;

                item.Value.BorderThickness =
                    new Thickness(2);
            }
        }

        private void RenameAccount_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not MenuItem menu)
            {
                return;
            }

            if (menu.Tag is not string accountId)
            {
                return;
            }

            AccountInfo? account =
                _accounts.FirstOrDefault(
                    item =>
                        item.Id == accountId);

            if (account == null)
            {
                return;
            }

            RenameWindow renameWindow =
                new RenameWindow(
                    account.Name)
                {
                    Owner =
                        this
                };

            if (renameWindow.ShowDialog() != true)
            {
                return;
            }

            account.Name =
                renameWindow.NewName;

            UpdateAccount(
                account);

            if (!_accountButtons.TryGetValue(
                    account.Id,
                    out Button? button))
            {
                return;
            }

            if (button.Content is not Grid rootGrid ||
                rootGrid.Children.Count == 0)
            {
                return;
            }

            if (rootGrid.Children[0] is not StackPanel panel ||
                panel.Children.Count < 2)
            {
                return;
            }

            if (panel.Children[1] is TextBlock nameText)
            {
                nameText.Text =
                    account.Name;
            }
        }
        private void UpdateUnreadBadge(
    string accountId,
    int unreadCount)
        {
            if (!_accountUnreadBadges.TryGetValue(
                    accountId,
                    out Border? badge))
            {
                return;
            }

            if (!_accountUnreadTexts.TryGetValue(
                    accountId,
                    out TextBlock? badgeText))
            {
                return;
            }

            if (unreadCount <= 0)
            {
                badge.Visibility =
                    Visibility.Collapsed;

                badgeText.Text =
                    string.Empty;

                return;
            }

            badgeText.Text =
                unreadCount > 99
                    ? "99+"
                    : unreadCount.ToString();

            badge.Visibility =
                Visibility.Visible;
        }

        private void DeleteAccount_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not MenuItem menu)
            {
                return;
            }

            if (menu.Tag is not string accountId)
            {
                return;
            }

            AccountInfo? account =
                _accounts.FirstOrDefault(
                    x =>
                        x.Id == accountId);

            if (account == null)
            {
                return;
            }

            MessageBoxResult result =
                MessageBox.Show(
                    $"确定删除【{account.Name}】？",
                    "删除账号",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result !=
                MessageBoxResult.Yes)
            {
                return;
            }

            DeleteAccount(
                account);
        }
        private void DeleteAccount(
    AccountInfo account)
        {
            if (_accountLoginStateMonitors.Remove(
                    account.Id,
                    out AccountLoginStateMonitor? monitor))
            {
                monitor.Dispose();
            }

            if (_accountViews.TryGetValue(
                    account.Id,
                    out WebView2? webView))
            {
                BrowserContainer.Children.Remove(
                    webView);

                webView.Dispose();

                _accountViews.Remove(
                    account.Id);
            }

            if (_accountButtons.TryGetValue(
                    account.Id,
                    out Button? button))
            {
                AccountPanel.Children.Remove(
                    button);

                _accountButtons.Remove(
                    account.Id);
                _accountUnreadBadges.Remove(
    account.Id);

                _accountUnreadTexts.Remove(
                    account.Id);
            }

            _accounts.Remove(
                account);

            DeleteAccountFromDatabase(
                account.Id);

            string profileFolder =
                Path.Combine(
                    _profilesFolder,
                    account.Id);

            try
            {
                if (Directory.Exists(
                    profileFolder))
                {
                    Directory.Delete(
                        profileFolder,
                        true);
                }
            }
            catch
            {
            }

            if (_accounts.Count > 0)
            {
                ShowAccount(
                    _accounts[0].Id);
            }
            else
            {
                EmptyMessage.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void RefreshAccount_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (sender is not MenuItem menu)
            {
                return;
            }

            if (menu.Tag is not string accountId)
            {
                return;
            }

            if (!_accountViews.TryGetValue(
                    accountId,
                    out WebView2? webView))
            {
                return;
            }

            try
            {
                webView.Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "刷新失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ClearCache_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not MenuItem menu)
            {
                return;
            }

            if (menu.Tag is not string accountId)
            {
                return;
            }

            if (!_accountViews.TryGetValue(
                    accountId,
                    out WebView2? webView))
            {
                return;
            }

            try
            {
                CoreWebView2BrowsingDataKinds cacheKinds =
                    CoreWebView2BrowsingDataKinds.DiskCache |
                    CoreWebView2BrowsingDataKinds.CacheStorage;

                await webView.CoreWebView2.Profile
                    .ClearBrowsingDataAsync(
                        cacheKinds);

                webView.Reload();

                MessageBox.Show(
                    "缓存已清理，应用已刷新。",
                    "完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "清理缓存失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void LoadAccounts()
        {
            _accounts.Clear();

            using SqliteConnection connection =
                new SqliteConnection(
                    GetConnectionString());

            connection.Open();

            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
            SELECT
                Id,
                Name,
                SortOrder,
                Platform
            FROM Accounts
            ORDER BY SortOrder;
            """;

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                AccountInfo account =
                    new AccountInfo
                    {
                        Id =
                            reader.GetString(0),

                        Name =
                            reader.GetString(1),

                        SortOrder =
                            reader.GetInt32(2),

                        Platform =
                            reader.GetString(3)
                    };

                _accounts.Add(
                    account);
            }
        }

        private void InsertAccount(
            AccountInfo account)
        {
            using SqliteConnection connection =
                new SqliteConnection(
                    GetConnectionString());

            connection.Open();

            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
            INSERT INTO Accounts
            (
                Id,
                Name,
                SortOrder,
                Platform
            )
            VALUES
            (
                $id,
                $name,
                $sort,
                $platform
            );
            """;

            command.Parameters.AddWithValue(
                "$id",
                account.Id);

            command.Parameters.AddWithValue(
                "$name",
                account.Name);

            command.Parameters.AddWithValue(
                "$sort",
                account.SortOrder);

            command.Parameters.AddWithValue(
                "$platform",
                account.Platform);

            command.ExecuteNonQuery();
        }

        private void UpdateAccount(
            AccountInfo account)
        {
            using SqliteConnection connection =
                new SqliteConnection(
                    GetConnectionString());

            connection.Open();

            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
            UPDATE Accounts
            SET
                Name = $name,
                SortOrder = $sort
            WHERE
                Id = $id;
            """;

            command.Parameters.AddWithValue(
                "$id",
                account.Id);

            command.Parameters.AddWithValue(
                "$name",
                account.Name);

            command.Parameters.AddWithValue(
                "$sort",
                account.SortOrder);

            command.ExecuteNonQuery();
        }

        private void DeleteAccountFromDatabase(
            string id)
        {
            using SqliteConnection connection =
                new SqliteConnection(
                    GetConnectionString());

            connection.Open();

            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
            """
            DELETE FROM Accounts
            WHERE Id = $id;
            """;

            command.Parameters.AddWithValue(
                "$id",
                id);

            command.ExecuteNonQuery();
        }

    }
    public class AccountInfo
    {
        public string Id { get; set; } =
            string.Empty;

        public string Name { get; set; } =
            string.Empty;

        public int SortOrder { get; set; }

        public string Platform { get; set; } =
            "LINE";
    }

    public class RenameWindow : Window
    {
        private readonly TextBox _nameBox;

        public string NewName =>
            _nameBox.Text.Trim();

        public RenameWindow(
            string oldName)
        {
            Title =
                "修改账号名称";

            Width = 360;

            Height = 175;

            ResizeMode =
                ResizeMode.NoResize;

            WindowStartupLocation =
                WindowStartupLocation.CenterOwner;

            Grid grid =
                new Grid
                {
                    Margin =
                        new Thickness(18)
                };

            grid.RowDefinitions.Add(
                new RowDefinition());

            grid.RowDefinitions.Add(
                new RowDefinition());

            grid.RowDefinitions.Add(
                new RowDefinition());

            TextBlock title =
                new TextBlock
                {
                    Text =
                        "请输入新的账号名称：",

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            8)
                };

            Grid.SetRow(
                title,
                0);

            _nameBox =
                new TextBox
                {
                    Text =
                        oldName,

                    Height =
                        30,

                    FontSize =
                        14
                };

            _nameBox.SelectAll();

            Grid.SetRow(
                _nameBox,
                1);

            StackPanel panel =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    HorizontalAlignment =
                        HorizontalAlignment.Right,

                    Margin =
                        new Thickness(
                            0,
                            14,
                            0,
                            0)
                };

            Button ok =
                new Button
                {
                    Content =
                        "确定",

                    Width =
                        70,

                    Height =
                        30,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            8,
                            0)
                };

            ok.Click +=
                (_, __) =>
                {
                    if (string.IsNullOrWhiteSpace(
                        _nameBox.Text))
                    {
                        MessageBox.Show(
                            "名称不能为空。");

                        return;
                    }

                    DialogResult = true;
                };

            Button cancel =
                new Button
                {
                    Content =
                        "取消",

                    Width =
                        70,

                    Height =
                        30
                };

            cancel.Click +=
                (_, __) =>
                {
                    DialogResult = false;
                };

            panel.Children.Add(
                ok);

            panel.Children.Add(
                cancel);

            Grid.SetRow(
                panel,
                2);

            grid.Children.Add(
                title);

            grid.Children.Add(
                _nameBox);

            grid.Children.Add(
                panel);

            Content =
                grid;

            Loaded +=
                (_, __) =>
                {
                    _nameBox.Focus();
                    _nameBox.SelectAll();
                };
        }
    }
}
