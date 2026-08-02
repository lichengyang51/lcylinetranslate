using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MultiChatManager2
{
    /// <summary>
    /// 独立监测每个账号自己的 WebView，避免账号切换时误用别的页面状态。
    /// 不确定页面状态时保持上一次结果；初始状态则显示为未登录。
    /// </summary>
    internal sealed class AccountLoginStateMonitor : IDisposable
    {
        private readonly WebView2 _webView;
        private readonly Button _accountButton;
        private readonly bool _isWhatsApp;
        private readonly DispatcherTimer _visiblePageTimer;

        private CoreWebView2? _coreWebView;
        private bool _isProbing;
        private bool _disposed;

        public AccountLoginStateMonitor(
            WebView2 webView,
            Button accountButton,
            string platform)
        {
            _webView = webView;
            _accountButton = accountButton;
            _isWhatsApp = string.Equals(
                platform,
                "WhatsApp",
                StringComparison.OrdinalIgnoreCase);

            _visiblePageTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(650)
            };

            _visiblePageTimer.Tick += VisiblePageTimerTick;
        }

        public void Start()
        {
            ApplyIconState(loggedIn: false);

            _accountButton.Click += AccountButtonClicked;
            _webView.CoreWebView2InitializationCompleted +=
                WebViewInitializationCompleted;
            _visiblePageTimer.Start();

            if (_webView.CoreWebView2 is not null)
            {
                AttachCoreWebView(_webView.CoreWebView2);
            }
        }

        private void WebViewInitializationCompleted(
            object? sender,
            CoreWebView2InitializationCompletedEventArgs eventArgs)
        {
            if (!_disposed && eventArgs.IsSuccess &&
                _webView.CoreWebView2 is not null)
            {
                AttachCoreWebView(_webView.CoreWebView2);
            }
        }

        private void AttachCoreWebView(CoreWebView2 coreWebView)
        {
            if (_disposed || _coreWebView == coreWebView)
            {
                return;
            }

            DetachCoreWebView();
            _coreWebView = coreWebView;
            coreWebView.NavigationCompleted += CoreWebViewNavigationCompleted;
            coreWebView.SourceChanged += CoreWebViewSourceChanged;

            ScheduleProbe(80);
            ScheduleProbe(450);
            ScheduleProbe(1200);
        }

        private void CoreWebViewNavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            ScheduleProbe(80);
            ScheduleProbe(450);
            ScheduleProbe(1200);
        }

        private void CoreWebViewSourceChanged(
            object? sender,
            CoreWebView2SourceChangedEventArgs eventArgs)
        {
            ScheduleProbe(250);
        }

        private void AccountButtonClicked(
            object sender,
            RoutedEventArgs eventArgs)
        {
            ScheduleProbe(80);
            ScheduleProbe(450);
        }

        private void VisiblePageTimerTick(
            object? sender,
            EventArgs eventArgs)
        {
            if (_webView.IsVisible &&
                _webView.ActualWidth > 10 &&
                _webView.ActualHeight > 10)
            {
                ScheduleProbe(0);
            }
        }

        private void ScheduleProbe(int delayMilliseconds)
        {
            _ = ProbeAfterDelayAsync(delayMilliseconds);
        }

        private async Task ProbeAfterDelayAsync(int delayMilliseconds)
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds);
            }

            await ProbeAsync();
        }

        private async Task ProbeAsync()
        {
            if (_disposed || _isProbing || _coreWebView is null)
            {
                return;
            }

            _isProbing = true;

            try
            {
                string result = await _coreWebView.ExecuteScriptAsync(
                    GetLoginStateProbeScript());

                if (string.Equals(
                    result.Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ApplyIconState(loggedIn: true);
                }
                else if (string.Equals(
                    result.Trim(),
                    "false",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ApplyIconState(loggedIn: false);
                }
            }
            catch
            {
                // 页面加载中时不改变图标，避免出现状态闪烁。
            }
            finally
            {
                _isProbing = false;
            }
        }

        private string GetLoginStateProbeScript()
        {
            return _isWhatsApp
                ? """
                  (() => {
                      const text = (document.body?.innerText || "")
                          .replace(/\s+/g, " ");
                      const hasChatUi =
                          document.querySelector(
                              "[data-testid='chat-list']," +
                              "[data-testid='chat-list-search']," +
                              "[data-testid='conversation-compose-box-input']," +
                              "[data-testid='msg-container']") !== null ||
                          /搜索或开始新聊天|搜索或发起新聊天|输入消息|search or start new chat|type a message/i
                              .test(text);
                      const hasLoginUi =
                          document.querySelector("canvas") !== null &&
                          /二维码|扫码|scan.*qr|link a device|use whatsapp on your computer|登入|登录/i
                              .test(text);
                      if (hasChatUi && !hasLoginUi) return true;
                      if (hasLoginUi) return false;
                      return null;
                  })();
                  """
                : """
                  (() => {
                      const text = (document.body?.innerText || "")
                          .replace(/\s+/g, " ");
                      const hasLoginUi =
                          document.querySelectorAll("input[type='password']")
                              .length > 0 ||
                          /二维码登录|扫码登录|邮箱地址|电子邮件地址|重置密码|ログイン|メールアドレス|QR code login/i
                              .test(text);
                      const hasChatUi =
                          document.querySelector(
                              "textarea,[contenteditable='true']") !== null ||
                          /聊天内容搜索|输入消息|好友|聊天|トーク|友だち|search chats/i
                              .test(text);
                      if (hasChatUi && !hasLoginUi) return true;
                      if (hasLoginUi) return false;
                      return null;
                  })();
                  """;
        }

        private void ApplyIconState(bool loggedIn)
        {
            if (_isWhatsApp)
            {
                Border? icon = FindTaggedBorder(
                    _accountButton,
                    "WhatsAppIcon");

                if (icon is not null)
                {
                    icon.Background = new SolidColorBrush(
                        loggedIn
                            ? Color.FromRgb(37, 211, 102)
                            : Color.FromRgb(112, 112, 112));
                }

                return;
            }

            Border? lineIcon = FindTaggedBorder(
                _accountButton,
                "LineIcon");

            if (lineIcon is not null)
            {
                lineIcon.Background = new SolidColorBrush(
                    loggedIn
                        ? Color.FromRgb(6, 199, 85)
                        : Color.FromRgb(112, 112, 112));
            }
        }

        private static Border? FindTaggedBorder(
            DependencyObject parent,
            string tag)
        {
            foreach (Border border in FindVisualChildren<Border>(parent))
            {
                if (string.Equals(
                    border.Tag as string,
                    tag,
                    StringComparison.Ordinal))
                {
                    return border;
                }
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(
            DependencyObject parent)
            where T : DependencyObject
        {
            int count;

            try
            {
                count = VisualTreeHelper.GetChildrenCount(parent);
            }
            catch
            {
                yield break;
            }

            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);

                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _visiblePageTimer.Stop();
            _accountButton.Click -= AccountButtonClicked;
            _webView.CoreWebView2InitializationCompleted -=
                WebViewInitializationCompleted;
            DetachCoreWebView();
        }

        private void DetachCoreWebView()
        {
            if (_coreWebView is null)
            {
                return;
            }

            _coreWebView.NavigationCompleted -=
                CoreWebViewNavigationCompleted;
            _coreWebView.SourceChanged -= CoreWebViewSourceChanged;
            _coreWebView = null;
        }
    }
}
