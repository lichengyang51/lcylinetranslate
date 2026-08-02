using System;
using System.Windows;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class AiReplySettingsWindow : Window
    {
        private readonly string _settingsPath;
        private AiReplySettings _settings = new();

        public AiReplySettingsWindow(string settingsPath, bool isDarkMode)
        {
            InitializeComponent();
            _settingsPath = settingsPath;
            ApplyTheme(isDarkMode);
            Loaded += AiReplySettingsWindow_Loaded;
        }

        private void AiReplySettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _settings = AiReplySettingsStore.Load(_settingsPath);
            ModelComboBox.SelectedValue = _settings.Model;

            if (ModelComboBox.SelectedIndex < 0)
            {
                ModelComboBox.SelectedValue = AiReplySettings.DefaultModel;
            }

            SavedKeyStatusTextBlock.Text = _settings.IsConfigured
                ? "✓ 已保存 API Key（为保护安全，不显示原 Key；留空可保持不变）"
                : "尚未保存 API Key。保存后才可以生成 AI 回复。";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool clearKey = ClearSavedApiKeyCheckBox.IsChecked == true;
            string newKey = ApiKeyPasswordBox.Password.Trim();

            if (clearKey)
            {
                _settings.ApiKey = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(newKey))
            {
                _settings.ApiKey = newKey;
            }

            if (!clearKey && string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                MessageBox.Show(
                    this,
                    "请输入 OpenAI API Key，或取消关闭窗口。",
                    "AI 智能回复",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _settings.Model = ModelComboBox.SelectedValue as string ??
                AiReplySettings.DefaultModel;

            try
            {
                AiReplySettingsStore.Save(_settingsPath, _settings);
                DialogResult = true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    "保存 AI 设置失败。\n\n" + exception.Message,
                    "AI 智能回复",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ApplyTheme(bool isDarkMode)
        {
            Resources["DialogBackgroundBrush"] = Brush(isDarkMode ? "#252525" : "#F7F8FA");
            Resources["DialogTextBrush"] = Brush(isDarkMode ? "#F4F4F5" : "#1F2937");
            Resources["DialogSecondaryTextBrush"] = Brush(isDarkMode ? "#B7BBC3" : "#6B7280");
            Resources["DialogControlBrush"] = Brush(isDarkMode ? "#303236" : "#FFFFFF");
            Resources["DialogHintBrush"] = Brush(isDarkMode ? "#202A35" : "#EEF6FF");
            Resources["DialogBorderBrush"] = Brush(isDarkMode ? "#4A4D53" : "#D1D5DB");
        }

        private static SolidColorBrush Brush(string color) =>
            new((Color)ColorConverter.ConvertFromString(color));
    }
}
