using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class YoudaoKeyWindow : Window
    {
        private readonly YoudaoTranslator _translator;

        private bool _isSynchronizingSecret;

        private bool _isTesting;

        public YoudaoKeyWindow(
            YoudaoTranslator translator)
        {
            InitializeComponent();

            _translator =
                translator ??
                throw new ArgumentNullException(
                    nameof(translator));

            Loaded +=
                YoudaoKeyWindow_Loaded;
        }

        private void YoudaoKeyWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadCurrentConfiguration();
        }

        private void LoadCurrentConfiguration()
        {
            try
            {
                _translator.LoadConfiguration();

                YoudaoConfig config =
                    _translator.GetConfiguration();

                AppKeyTextBox.Text =
                    config.AppKey;

                SetSecretValue(
                    config.AppSecret);

                UpdateConfigurationStatus(
                    _translator.IsConfigured);
            }
            catch (Exception exception)
            {
                AppKeyTextBox.Text =
                    string.Empty;

                SetSecretValue(
                    string.Empty);

                UpdateConfigurationStatus(
                    false);

                MessageBox.Show(
                    this,
                    "读取有道翻译配置失败。\n\n" +
                    exception.Message,
                    "有道翻译配置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SetSecretValue(
            string value)
        {
            _isSynchronizingSecret =
                true;

            try
            {
                AppSecretPasswordBox.Password =
                    value;

                AppSecretTextBox.Text =
                    value;
            }
            finally
            {
                _isSynchronizingSecret =
                    false;
            }
        }

        private string GetCurrentSecret()
        {
            if (ShowAppSecretCheckBox.IsChecked ==
                true)
            {
                return AppSecretTextBox.Text.Trim();
            }

            return AppSecretPasswordBox.Password.Trim();
        }

        private void UpdateConfigurationStatus(
            bool configured)
        {
            if (configured)
            {
                ConfigStatusTextBlock.Text =
                    "✓ 已配置";

                ConfigStatusTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            4,
                            120,
                            87));

                ConfigStatusBorder.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            236,
                            253,
                            245));

                ConfigStatusBorder.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            167,
                            243,
                            208));

                return;
            }

            ConfigStatusTextBlock.Text =
                "⚠ 尚未配置";

            ConfigStatusTextBlock.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        146,
                        64,
                        14));

            ConfigStatusBorder.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        255,
                        251,
                        235));

            ConfigStatusBorder.BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        253,
                        230,
                        138));
        }

        private void ShowAppSecretCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_isSynchronizingSecret)
            {
                return;
            }

            _isSynchronizingSecret =
                true;

            try
            {
                if (ShowAppSecretCheckBox.IsChecked ==
                    true)
                {
                    AppSecretTextBox.Text =
                        AppSecretPasswordBox.Password;

                    AppSecretPasswordBox.Visibility =
                        Visibility.Collapsed;

                    AppSecretTextBox.Visibility =
                        Visibility.Visible;

                    AppSecretTextBox.Focus();

                    AppSecretTextBox.CaretIndex =
                        AppSecretTextBox.Text.Length;
                }
                else
                {
                    AppSecretPasswordBox.Password =
                        AppSecretTextBox.Text;

                    AppSecretTextBox.Visibility =
                        Visibility.Collapsed;

                    AppSecretPasswordBox.Visibility =
                        Visibility.Visible;

                    AppSecretPasswordBox.Focus();
                }
            }
            finally
            {
                _isSynchronizingSecret =
                    false;
            }
        }

        private void AppSecretPasswordBox_PasswordChanged(
            object sender,
            RoutedEventArgs e)
        {
            if (_isSynchronizingSecret)
            {
                return;
            }

            _isSynchronizingSecret =
                true;

            try
            {
                AppSecretTextBox.Text =
                    AppSecretPasswordBox.Password;
            }
            finally
            {
                _isSynchronizingSecret =
                    false;
            }
        }

        private void AppSecretTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_isSynchronizingSecret)
            {
                return;
            }

            _isSynchronizingSecret =
                true;

            try
            {
                AppSecretPasswordBox.Password =
                    AppSecretTextBox.Text;
            }
            finally
            {
                _isSynchronizingSecret =
                    false;
            }
        }

        private bool ValidateInputs(
            out string appKey,
            out string appSecret)
        {
            appKey =
                AppKeyTextBox.Text.Trim();

            appSecret =
                GetCurrentSecret();

            if (string.IsNullOrWhiteSpace(
                    appKey))
            {
                MessageBox.Show(
                    this,
                    "请输入有道 AppKey。",
                    "有道翻译配置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                AppKeyTextBox.Focus();

                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    appSecret))
            {
                MessageBox.Show(
                    this,
                    "请输入有道 AppSecret。",
                    "有道翻译配置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (ShowAppSecretCheckBox.IsChecked ==
                    true)
                {
                    AppSecretTextBox.Focus();
                }
                else
                {
                    AppSecretPasswordBox.Focus();
                }

                return false;
            }

            return true;
        }
        private async void TestButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_isTesting)
            {
                return;
            }

            if (!ValidateInputs(
                    out string appKey,
                    out string appSecret))
            {
                return;
            }

            _isTesting = true;

            TestButton.IsEnabled = false;

            try
            {
                ConfigStatusTextBlock.Text =
                    "正在测试连接...";

                ConfigStatusTextBlock.Foreground =
                    Brushes.DodgerBlue;

                _translator.SaveConfiguration(
                    appKey,
                    appSecret);

                _translator.LoadConfiguration();

                await _translator
                    .TranslateJapaneseToChineseAsync(
                        "こんにちは");

                UpdateConfigurationStatus(
                    true);

                MessageBox.Show(
                    this,
                    "连接测试成功！\n\n以后翻译将使用当前填写的有道账号。",
                    "测试成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                UpdateConfigurationStatus(
                    false);

                MessageBox.Show(
                    this,
                    "连接测试失败。\n\n" +
                    exception.Message,
                    "测试失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isTesting = false;

                TestButton.IsEnabled = true;
            }
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!ValidateInputs(
                    out string appKey,
                    out string appSecret))
            {
                return;
            }

            try
            {
                _translator.SaveConfiguration(
                    appKey,
                    appSecret);

                UpdateConfigurationStatus(
                    true);

                MessageBox.Show(
                    this,
                    "保存成功。",
                    "有道翻译配置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;

                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    "保存失败。\n\n" +
                    exception.Message,
                    "有道翻译配置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result =
                MessageBox.Show(
                    this,
                    "确定清空当前有道配置？\n\n清空后软件将停止自动翻译。",
                    "确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result !=
                MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _translator.ClearConfiguration();

                AppKeyTextBox.Clear();

                SetSecretValue(
                    string.Empty);

                UpdateConfigurationStatus(
                    false);

                MessageBox.Show(
                    this,
                    "已清空配置。",
                    "有道翻译配置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "清空失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;

            Close();
        }
    }
}