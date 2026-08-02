using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class LicenseStatusWindow : Window
    {
        private readonly string _licenseFilePath;

        private readonly LicenseManager _licenseManager;

        public LicenseStatusWindow()
        {
            InitializeComponent();

            string dataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MultiChatManager2");

            _licenseFilePath =
                Path.Combine(
                    dataFolder,
                    "license.json");

            string functionUrl =
                "https://iyadtuwabsmiohkyfvqv.supabase.co/functions/v1/verify-license";

            string publishableKey =
                "sb_publishable_7VocoBlUphbJLxqq62_MXg_5vLWlnHF";

            _licenseManager =
                new LicenseManager(
                    dataFolder,
                    functionUrl,
                    publishableKey);

            Loaded +=
                LicenseStatusWindow_Loaded;
        }

        private async void LicenseStatusWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshLicenseAsync();
        }

        private async System.Threading.Tasks.Task RefreshLicenseAsync()
        {
            RefreshStatusTextBlock.Text =
                "正在连接授权服务器...";

            try
            {
                LicenseResult result =
                    await _licenseManager
                        .VerifySavedLicenseAsync();

                if (result.Success)
                {
                    RefreshStatusTextBlock.Text =
                        "已同步最新授权信息";

                    LoadLocalLicense();

                    return;
                }

                RefreshStatusTextBlock.Text =
                    "服务器不可用，显示本地缓存";

                LoadLocalLicense();
            }
            catch
            {
                RefreshStatusTextBlock.Text =
                    "服务器不可用，显示本地缓存";

                LoadLocalLicense();
            }
        }

        private void LoadLocalLicense()
        {
            try
            {
                if (!File.Exists(
                        _licenseFilePath))
                {
                    ShowNotActivated(
                        "本机尚未保存授权信息。");

                    return;
                }

                string json =
                    File.ReadAllText(
                        _licenseFilePath,
                        Encoding.UTF8);

                LocalLicenseData? data =
                    JsonSerializer.Deserialize<LocalLicenseData>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                if (data == null)
                {
                    ShowNotActivated(
                        "授权文件无效。");

                    return;
                }

                UpdateUi(data);
            }
            catch (Exception exception)
            {
                ShowNotActivated(
                    exception.Message);
            }
        }

        private void UpdateUi(
            LocalLicenseData data)
        {
            LicenseKeyTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    data.LicenseKey)
                ? "-"
                : data.LicenseKey;

            DeviceIdTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    data.DeviceId)
                ? "-"
                : data.DeviceId;

            ActivatedAtTextBlock.Text =
                FormatDateTime(
                    data.SavedAt);

            if (string.IsNullOrWhiteSpace(
                    data.ExpireAt))
            {
                LicenseTypeTextBlock.Text =
                    "永久授权";

                ExpireAtTextBlock.Text =
                    "永久有效";

                RemainingTimeTextBlock.Text =
                    "∞";

                StatusIconTextBlock.Text =
                    "🟢";

                StatusTextBlock.Text =
                    "已激活";

                StatusTextBlock.Foreground =
                    Brushes.Green;

                ServerTimeTextBlock.Text =
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss");

                return;
            }

            if (!DateTimeOffset.TryParse(
                    data.ExpireAt,
                    out DateTimeOffset expireTime))
            {
                LicenseTypeTextBlock.Text =
                    "限时授权";

                ExpireAtTextBlock.Text =
                    data.ExpireAt;

                RemainingTimeTextBlock.Text =
                    "-";

                StatusIconTextBlock.Text =
                    "🟢";

                StatusTextBlock.Text =
                    "已激活";

                StatusTextBlock.Foreground =
                    Brushes.Green;

                ServerTimeTextBlock.Text =
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss");

                return;
            }

            ExpireAtTextBlock.Text =
                expireTime
                    .ToLocalTime()
                    .ToString(
                        "yyyy-MM-dd HH:mm:ss");

            LicenseTypeTextBlock.Text =
                "限时授权";

            ServerTimeTextBlock.Text =
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss");

            UpdateRemainingTime(
                expireTime);
        }
        private void UpdateRemainingTime(
            DateTimeOffset expireTime)
        {
            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            TimeSpan remaining =
                expireTime - now;

            if (remaining <= TimeSpan.Zero)
            {
                StatusIconTextBlock.Text =
                    "🔴";

                StatusTextBlock.Text =
                    "已到期";

                StatusTextBlock.Foreground =
                    Brushes.Red;

                StatusBorder.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            254,
                            242,
                            242));

                StatusBorder.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            254,
                            202,
                            202));

                RemainingTimeTextBlock.Text =
                    "已到期";

                RemainingTimeTextBlock.Foreground =
                    Brushes.Red;

                return;
            }

            int remainingDays =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        remaining.TotalDays));

            RemainingTimeTextBlock.Text =
                remainingDays + " 天";

            if (remainingDays <= 7)
            {
                StatusIconTextBlock.Text =
                    "🟠";

                StatusTextBlock.Text =
                    "即将到期，请及时续费";

                StatusTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            194,
                            65,
                            12));

                StatusBorder.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            247,
                            237));

                StatusBorder.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            254,
                            215,
                            170));

                RemainingTimeTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            194,
                            65,
                            12));

                return;
            }

            if (remainingDays <= 30)
            {
                StatusIconTextBlock.Text =
                    "🟡";

                StatusTextBlock.Text =
                    "授权有效，即将到期";

                StatusTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            161,
                            98,
                            7));

                StatusBorder.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            254,
                            252,
                            232));

                StatusBorder.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            253,
                            230,
                            138));

                RemainingTimeTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            161,
                            98,
                            7));

                return;
            }

            StatusIconTextBlock.Text =
                "🟢";

            StatusTextBlock.Text =
                "已激活";

            StatusTextBlock.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        4,
                        120,
                        87));

            StatusBorder.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        236,
                        253,
                        245));

            StatusBorder.BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        167,
                        243,
                        208));

            RemainingTimeTextBlock.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        4,
                        120,
                        87));
        }

        private void ShowNotActivated(
            string message)
        {
            StatusIconTextBlock.Text =
                "🔴";

            StatusTextBlock.Text =
                "未激活";

            StatusTextBlock.Foreground =
                Brushes.Red;

            StatusBorder.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        254,
                        242,
                        242));

            StatusBorder.BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        254,
                        202,
                        202));

            RefreshStatusTextBlock.Text =
                message;

            LicenseKeyTextBlock.Text =
                "-";

            DeviceIdTextBlock.Text =
                "-";

            ActivatedAtTextBlock.Text =
                "-";

            ExpireAtTextBlock.Text =
                "-";

            LicenseTypeTextBlock.Text =
                "-";

            RemainingTimeTextBlock.Text =
                "-";

            ServerTimeTextBlock.Text =
                "-";
        }

        private static string FormatDateTime(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "-";
            }

            if (!DateTimeOffset.TryParse(
                    value,
                    out DateTimeOffset dateTime))
            {
                return value;
            }

            return dateTime
                .ToLocalTime()
                .ToString(
                    "yyyy-MM-dd HH:mm:ss");
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(
            EventArgs e)
        {
            _licenseManager.Dispose();

            base.OnClosed(e);
        }
    }

    public sealed class LocalLicenseData
    {
        public string LicenseKey { get; set; } =
            string.Empty;

        public string DeviceId { get; set; } =
            string.Empty;

        public string? ExpireAt { get; set; }

        public string? SavedAt { get; set; }
    }
}