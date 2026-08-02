using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiChatManager2
{
    public partial class AiReplyWindow : Window
    {
        private static readonly IReadOnlyDictionary<string, string[]> SpecificGoalsByCategory =
            new Dictionary<string, string[]>
            {
                ["日常互动类"] = ["日常闲聊", "关心对方", "赞美对方", "认可观点", "分享感受", "幽默回应"],
                ["关系推进类"] = ["拉近距离", "建立信任", "暧昧回复", "表达在意", "制造共同感", "留下期待", "邀约或做约定"],
                ["话题控制类"] = ["延续拓展", "引导对方多说", "深入了解", "转移话题", "重新激活话题", "自然结束话题"],
                ["情绪处理类"] = ["安慰对方", "接住情绪", "缓解尴尬", "降低戒备", "消除误会", "冲突降温", "道歉修复"],
                ["信息与判断类"] = ["询问真实想法", "了解顾虑", "确认态度", "试探兴趣", "澄清信息", "判断是否愿意继续聊"],
                ["行动推进类"] = ["引导回复", "引导交换联系方式", "引导分享照片或近况", "邀请见面", "推动下一步", "礼貌拒绝", "设置边界"]
            };

        private AiReplyRequest _request;
        private readonly AiReplySettings _settings;
        private readonly OpenAiReplyService _service;
        private CancellationTokenSource? _generationCancellation;
        private int _requestVersion;

        public AiReplyWindow(
            AiReplyRequest request,
            AiReplySettings settings,
            OpenAiReplyService service,
            bool isDarkMode)
        {
            InitializeComponent();
            _request = request;
            _settings = settings;
            _service = service;
            ApplyTheme(isDarkMode);
            ApplyRequest(request, "选择沟通方式后，点击生成回复。");
            SetDefaultSelections();
            Closed += (_, __) => _generationCancellation?.Cancel();
            Deactivated += (_, __) => SpecificGoalsPopup.IsOpen = false;
        }

        /// <summary>
        /// 复用同一个 AI 回复窗口。切换到另一条消息时，旧生成任务会取消，
        /// 结果不会误写到新消息上。
        /// </summary>
        public void UpdateRequest(AiReplyRequest request)
        {
            _generationCancellation?.Cancel();
            ApplyRequest(request, "已切换到新消息，请重新生成回复。");
            GenerateButton.IsEnabled = true;
            Activate();
        }

        private void ApplyRequest(AiReplyRequest request, string status)
        {
            _request = request;
            _requestVersion++;
            MessagePreviewTextBlock.Text =
                string.IsNullOrWhiteSpace(request.TranslationText)
                    ? request.OriginalText
                    : request.TranslationText;
            GeneratedReplyTextBox.Clear();
            CopyButton.IsEnabled = false;
            StatusTextBlock.Text = status;
        }

        private void SetDefaultSelections()
        {
            GoalCategoryComboBox.SelectedIndex = -1;
            RelationshipComboBox.SelectedIndex = 1;
            ReplyStyleComboBox.SelectedIndex = 0;
            EmotionExpressionComboBox.SelectedIndex = 1;
            ReplyScopeComboBox.SelectedIndex = 0;
            PaceComboBox.SelectedIndex = 1;
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            string goalCategory = SelectedText(GoalCategoryComboBox);
            List<string> specificGoals = GetSelectedSpecificGoals();
            if (string.IsNullOrWhiteSpace(goalCategory) || specificGoals.Count == 0)
            {
                StatusTextBlock.Text = "请先选择目标分类和至少 1 个具体目标（最多 2 项）。";
                return;
            }

            _generationCancellation?.Cancel();
            _generationCancellation = new CancellationTokenSource();
            int requestVersion = _requestVersion;
            AiReplyRequest request = _request;

            GenerateButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            StatusTextBlock.Text = "正在生成回复…";
            GeneratedReplyTextBox.Clear();

            try
            {
                string reply = await _service.GenerateReplyAsync(
                    _settings,
                    request,
                    new AiReplyOptions(
                        goalCategory,
                        specificGoals,
                        SelectedText(RelationshipComboBox),
                        SelectedText(ReplyStyleComboBox),
                        SelectedText(EmotionExpressionComboBox),
                        SelectedText(ReplyScopeComboBox),
                        SelectedText(PaceComboBox)),
                    _generationCancellation.Token);

                if (requestVersion != _requestVersion)
                {
                    return;
                }

                GeneratedReplyTextBox.Text = reply;
                CopyButton.IsEnabled = true;
                StatusTextBlock.Text = "已生成。确认内容后可复制。";
            }
            catch (OperationCanceledException)
            {
                if (requestVersion == _requestVersion)
                {
                    StatusTextBlock.Text = "已取消本次生成。";
                }
            }
            catch (OpenAiReplyException exception)
            {
                if (requestVersion == _requestVersion)
                {
                    StatusTextBlock.Text = exception.Message;
                }
            }
            catch (Exception)
            {
                if (requestVersion == _requestVersion)
                {
                    StatusTextBlock.Text = "生成失败，请检查网络后重试。";
                }
            }
            finally
            {
                if (requestVersion == _requestVersion)
                {
                    GenerateButton.IsEnabled = true;
                }
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GeneratedReplyTextBox.Text))
            {
                return;
            }

            Clipboard.SetText(GeneratedReplyTextBox.Text);
            StatusTextBlock.Text = "已复制到剪贴板。";
        }

        private static string SelectedText(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        }

        private void GoalCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSpecificGoalOptions();
        }

        private void UpdateSpecificGoalOptions()
        {
            SpecificGoalPanel.Children.Clear();
            SpecificGoalsPopup.IsOpen = false;

            string category = SelectedText(GoalCategoryComboBox);
            if (!SpecificGoalsByCategory.TryGetValue(category, out string[]? goals))
            {
                SpecificGoalSelectionHintTextBlock.Text = "请先选择目标分类";
                SpecificGoalsSelectionTextBlock.Text = "请先选择目标分类";
                SpecificGoalsDropDownButton.IsEnabled = false;
                return;
            }

            SpecificGoalsDropDownButton.IsEnabled = true;
            foreach (string goal in goals)
            {
                CheckBox checkBox = new()
                {
                    Content = goal,
                    Style = (Style)FindResource("SpecificGoalCheckBoxStyle")
                };
                checkBox.Checked += SpecificGoalCheckBox_Changed;
                checkBox.Unchecked += SpecificGoalCheckBox_Changed;
                SpecificGoalPanel.Children.Add(checkBox);
            }

            SpecificGoalSelectionHintTextBlock.Text = "已选择 0/2 项";
            SpecificGoalsSelectionTextBlock.Text = "请选择具体目标";
        }

        private void SpecificGoalCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox &&
                checkBox.IsChecked == true &&
                GetSelectedSpecificGoals().Count > 2)
            {
                checkBox.IsChecked = false;
                StatusTextBlock.Text = "具体目标最多只能选择 2 项。";
                return;
            }

            int selectedCount = GetSelectedSpecificGoals().Count;
            SpecificGoalSelectionHintTextBlock.Text = $"已选择 {selectedCount}/2 项";
            SpecificGoalsSelectionTextBlock.Text = selectedCount == 0
                ? "请选择具体目标"
                : string.Join("、", GetSelectedSpecificGoals());
        }

        private void SpecificGoalsDropDownButton_Click(object sender, RoutedEventArgs e)
        {
            SpecificGoalsPopup.IsOpen = !SpecificGoalsPopup.IsOpen;
        }

        private void AiReplyWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!SpecificGoalsPopup.IsOpen ||
                e.OriginalSource is not DependencyObject source ||
                IsDescendantOf(source, SpecificGoalsDropDownButton))
            {
                return;
            }

            SpecificGoalsPopup.IsOpen = false;
        }

        private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
        {
            for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        private List<string> GetSelectedSpecificGoals()
        {
            List<string> goals = [];
            foreach (UIElement child in SpecificGoalPanel.Children)
            {
                if (child is CheckBox { IsChecked: true, Content: string goal })
                {
                    goals.Add(goal);
                }
            }

            return goals;
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
