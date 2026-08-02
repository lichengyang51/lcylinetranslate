using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class ActivationWindow : Window
    {
        private readonly LicenseManager
            _licenseManager;

        public ActivationWindow(
            LicenseManager licenseManager)
        {
            InitializeComponent();

            _licenseManager =
                licenseManager;

            DeviceIdTextBlock.Text =
                _licenseManager.DeviceId;

            Loaded +=
                ActivationWindow_Loaded;

            LicenseKeyTextBox.KeyDown +=
                LicenseKeyTextBox_KeyDown;
        }

        private void ActivationWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            string? savedLicenseKey =
                _licenseManager.SavedLicenseKey;

            if (!string.IsNullOrWhiteSpace(
                    savedLicenseKey))
            {
                LicenseKeyTextBox.Text =
                    savedLicenseKey;
            }

            LicenseKeyTextBox.Focus();

            LicenseKeyTextBox.SelectAll();
        }

        private async void LicenseKeyTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled =
                true;

            await ActivateLicenseAsync();
        }

        private async void ActivateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ActivateLicenseAsync();
        }

        private async Task ActivateLicenseAsync()
        {
            string licenseKey =
                LicenseKeyTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(
                    licenseKey))
            {
                ShowStatus(
                    "请输入激活码。",
                    false);

                LicenseKeyTextBox.Focus();

                return;
            }

            ActivateButton.IsEnabled =
                false;

            LicenseKeyTextBox.IsEnabled =
                false;

            ShowStatus(
                "正在连接授权服务器，请稍候……",
                true);

            try
            {
                LicenseResult result =
                    await _licenseManager
                        .ActivateAsync(
                            licenseKey);

                if (!result.Success)
                {
                    ShowStatus(
                        result.Message,
                        false);

                    return;
                }

                string successMessage;

                if (result.Permanent)
                {
                    successMessage =
                        "激活成功，当前授权为永久授权。";
                }
                else if (!string.IsNullOrWhiteSpace(
                             result.ExpireAt) &&
                         DateTimeOffset.TryParse(
                             result.ExpireAt,
                             out DateTimeOffset expireTime))
                {
                    successMessage =
                        "激活成功，有效期至：" +
                        expireTime
                            .ToLocalTime()
                            .ToString(
                                "yyyy-MM-dd HH:mm");
                }
                else
                {
                    successMessage =
                        "激活成功。";
                }

                ShowStatus(
                    successMessage,
                    true);

                await Task.Delay(
                    700);

                DialogResult =
                    true;

                Close();
            }
            catch (Exception exception)
            {
                ShowStatus(
                    "激活失败：" +
                    exception.Message,
                    false);
            }
            finally
            {
                ActivateButton.IsEnabled =
                    true;

                LicenseKeyTextBox.IsEnabled =
                    true;
            }
        }

        private void ShowStatus(
            string message,
            bool success)
        {
            StatusTextBlock.Text =
                message;

            StatusTextBlock.Foreground =
                new SolidColorBrush(
                    success
                        ? Color.FromRgb(
                            22,
                            163,
                            74)
                        : Color.FromRgb(
                            217,
                            54,
                            62));
        }
    }
}