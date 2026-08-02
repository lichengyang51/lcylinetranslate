using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    /// <summary>
    /// 将 WhatsApp Web 中的日语消息交给现有的有道翻译服务，并把中文
    /// 译文直接显示在原消息下方。网页 DOM 会不定期变化，因此脚本优先
    /// 使用 WhatsApp 的 data-testid，再以消息气泡的位置作为后备判断。
    /// </summary>
    public sealed class WhatsAppTranslationManager
    {
        private readonly YoudaoTranslator _translator;
        private readonly TranslationPresentationSettings
            _presentationSettings;

        private readonly ConcurrentDictionary<string, byte>
            _processing =
                new(StringComparer.Ordinal);

        public WhatsAppTranslationManager(
            YoudaoTranslator translator,
            TranslationPresentationSettings presentationSettings)
        {
            _translator =
                translator;

            _presentationSettings =
                presentationSettings;
        }

        public async Task AttachAsync(
            WebView2 webView,
            string accountId)
        {
            webView.CoreWebView2.WebMessageReceived +=
                async (_, eventArgs) =>
                    await HandleAsync(
                        webView,
                        accountId,
                        eventArgs);

            await webView.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(
                    GetWhatsAppTranslationScript());

            webView.CoreWebView2.NavigationCompleted +=
                async (_, __) =>
                {
                    try
                    {
                        await webView.CoreWebView2
                            .ExecuteScriptAsync(
                                GetWhatsAppTranslationScript());
                    }
                    catch
                    {
                    }
                };
        }

        private async Task HandleAsync(
            WebView2 webView,
            string accountId,
            CoreWebView2WebMessageReceivedEventArgs eventArgs)
        {
            string messageId =
                string.Empty;

            bool forceRefresh =
                false;

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(
                        eventArgs.TryGetWebMessageAsString());

                JsonElement root =
                    document.RootElement;

                if (!root.TryGetProperty(
                        "type",
                        out JsonElement typeElement) ||
                    typeElement.GetString() !=
                        "whatsAppTranslationRequest" ||
                    !root.TryGetProperty(
                        "messageId",
                        out JsonElement idElement) ||
                    !root.TryGetProperty(
                        "text",
                        out JsonElement textElement))
                {
                    return;
                }

                messageId =
                    idElement.GetString() ??
                    string.Empty;

                string text =
                    textElement.GetString() ??
                    string.Empty;

                forceRefresh =
                    root.TryGetProperty(
                        "forceRefresh",
                        out JsonElement forceElement) &&
                    forceElement.ValueKind ==
                        JsonValueKind.True;

                if (string.IsNullOrWhiteSpace(
                        messageId) ||
                    string.IsNullOrWhiteSpace(
                        text))
                {
                    return;
                }

                // 顶部“关闭自动翻译”勾选时，仅阻止自动请求；手动重试
                // 仍然可以调用翻译服务。
                if (MainWindow.TranslateVisibleOnly &&
                    !forceRefresh)
                {
                    return;
                }

                string processingKey =
                    accountId +
                    ":" +
                    messageId;

                if (!_processing.TryAdd(
                        processingKey,
                        0))
                {
                    await ReportFailureAsync(
                        webView,
                        messageId,
                        "这条消息正在翻译，请稍候再试。");

                    return;
                }

                try
                {
                    string? translation =
                        await _translator
                            .TranslateJapaneseToChineseAsync(
                                text,
                                forceRefresh: forceRefresh);

                    if (string.IsNullOrWhiteSpace(
                            translation))
                    {
                        await ReportFailureAsync(
                            webView,
                            messageId,
                            "没有得到译文，请点击 ↻ 重试。");

                        return;
                    }

                    await ExecuteAsync(
                        webView,
                        "(()=>{window.__mcmWhatsAppApplyTranslation?.(" +
                        JsonSerializer.Serialize(messageId) +
                        "," +
                        JsonSerializer.Serialize(translation) +
                        ");})();");
                }
                finally
                {
                    _processing.TryRemove(
                        processingKey,
                        out _);
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(
                        messageId))
                {
                    await ReportFailureAsync(
                        webView,
                        messageId,
                        "翻译失败，请点击 ↻ 重试。");
                }
            }
        }

        private static async Task ReportFailureAsync(
            WebView2 webView,
            string messageId,
            string message)
        {
            try
            {
                await ExecuteAsync(
                    webView,
                    "(()=>{window.__mcmWhatsAppTranslationFailed?.(" +
                    JsonSerializer.Serialize(messageId) +
                    "," +
                    JsonSerializer.Serialize(message) +
                    ");})();");
            }
            catch
            {
            }
        }

        private static async Task ExecuteAsync(
            WebView2 webView,
            string script)
        {
            await webView.Dispatcher.InvokeAsync(
                async () =>
                {
                    if (webView.CoreWebView2 is not null)
                    {
                        await webView.CoreWebView2
                            .ExecuteScriptAsync(
                                script);
                    }
                });
        }

        private string GetWhatsAppTranslationScript() =>
        $$$$$$$$$$"""
        (() => {
          const initialPresentation = {{{{{{{{{{JsonSerializer.Serialize(_presentationSettings)}}}}}}}}}};
          if (window.__mcmWhatsAppTranslatorInstalled) {
            window.__mcmUpdateWhatsAppTranslationSettings?.(
              initialPresentation);
            window.__mcmWhatsAppScan?.();
            return;
          }

          window.__mcmWhatsAppTranslatorInstalled = true;

          const messages = new Map();
          const pending = new Set();
          let nextId = 1;
          let scanTimer = null;
          let presentation = normalizePresentation(initialPresentation);

          const clean = value => String(value || "")
            .replace(/\u200B/g, "")
            .replace(/\r/g, "")
            .replace(/[ \t]+/g, " ")
            .replace(/\n{3,}/g, "\n\n")
            .trim();

          const hasJapanese = value =>
            /[\u3040-\u30ff\u31f0-\u31ff]/.test(value || "");

          function normalizePresentation(value) {
            const fontSize = Math.min(
              18,
              Math.max(12, Number(value?.FontSize) || 12));

            return {
              fontSize,
              displayMode: value?.DisplayMode === "TranslationOnly"
                ? "TranslationOnly"
                : "BelowOriginal"
            };
          }

          function isVisible(element) {
            if (!(element instanceof HTMLElement)) return false;
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            return style.display !== "none" &&
              style.visibility !== "hidden" &&
              Number(style.opacity) !== 0 &&
              rect.width > 0 && rect.height > 0 &&
              rect.bottom > 70 && rect.top < innerHeight - 35;
          }

          function isOwnUi(element) {
            return !element || !!element.closest(
              "input,textarea,button,header,footer,nav," +
              "[contenteditable='true'],.mcm-wa-translation," +
              ".mcm-wa-translate-copy,.mcm-wa-translate-retry," +
              ".mcm-wa-translation-status");
          }

          function bubbleFor(element) {
            const known = element.closest("[data-testid='msg-container']");
            if (known) return known;

            let current = element;
            for (let depth = 0;
              depth < 10 && current;
              depth++, current = current.parentElement) {
              if (!(current instanceof HTMLElement)) break;

              const rect = current.getBoundingClientRect();
              const looksLikeMessage =
                (current.hasAttribute("data-id") ||
                  current.querySelector("[data-pre-plain-text]") !== null) &&
                rect.left >= innerWidth * .25 &&
                rect.width >= 35 &&
                rect.width <= innerWidth * .76 &&
                rect.height >= 18;

              if (looksLikeMessage) return current;
            }

            return null;
          }

          function idFor(bubble) {
            let id = bubble.getAttribute("data-mcm-wa-message-id");
            if (id) {
              messages.set(id, bubble);
              return id;
            }

            const realId = bubble.getAttribute("data-id") ||
              bubble.querySelector("[data-id]")?.getAttribute("data-id");

            id = realId
              ? "mcm-wa-" + realId
              : "mcm-wa-" + Date.now() + "-" + nextId++;

            bubble.setAttribute("data-mcm-wa-message-id", id);
            messages.set(id, bubble);
            return id;
          }

          function findBubble(id) {
            return messages.get(id) ||
              document.querySelector(
                '[data-mcm-wa-message-id="' +
                CSS.escape(id) +
                '"]');
          }

          function sourceFor(bubble) {
            const remembered = bubble.getAttribute(
              "data-mcm-wa-source");
            if (remembered) return remembered;

            const clone = bubble.cloneNode(true);
            clone.querySelectorAll(
              ".mcm-wa-translation,.mcm-wa-translate-retry," +
              ".mcm-wa-translate-copy,.mcm-wa-translation-status")
              .forEach(item => item.remove());

            const parts = Array.from(
              clone.querySelectorAll("span.selectable-text"))
              .map(item => clean(item.innerText))
              .filter(Boolean);

            return clean(parts.length
              ? parts.join("\n")
              : clone.innerText);
          }

          function rememberSource(bubble) {
            const source = sourceFor(bubble);
            if (source) {
              bubble.setAttribute("data-mcm-wa-source", source);
            }
          }

          function sourceElements(bubble) {
            const selectable = Array.from(
              bubble.querySelectorAll("span.selectable-text"));

            if (selectable.length) return selectable;

            const messageText = Array.from(
              bubble.querySelectorAll("[data-testid='msg-text']"));

            if (messageText.length) return messageText;

            return Array.from(bubble.children).filter(item =>
              item instanceof HTMLElement &&
              !item.classList.contains("mcm-wa-translation") &&
              !item.classList.contains("mcm-wa-translate-copy") &&
              !item.classList.contains("mcm-wa-translate-retry") &&
              !item.classList.contains("mcm-wa-translation-status"));
          }

          function setSourceVisibility(bubble,
            displayMode = presentation.displayMode) {
            const hide = displayMode === "TranslationOnly";

            sourceElements(bubble).forEach(item => {
              if (hide) {
                if (!item.hasAttribute(
                    "data-mcm-wa-original-display")) {
                  item.setAttribute(
                    "data-mcm-wa-original-display",
                    item.style.display);
                }

                item.style.display = "none";
              } else if (item.hasAttribute(
                  "data-mcm-wa-original-display")) {
                item.style.display = item.getAttribute(
                  "data-mcm-wa-original-display") || "";
                item.removeAttribute(
                  "data-mcm-wa-original-display");
              }
            });
          }

          function applyPresentation(bubble, translation) {
            translation.style.fontSize = presentation.fontSize + "px";
            setSourceVisibility(bubble);
          }

          function status(bubble, text, isError) {
            let item = bubble.querySelector(
              ":scope .mcm-wa-translation-status");

            if (!item) {
              item = document.createElement("div");
              item.className = "mcm-wa-translation-status";
              bubble.appendChild(item);
            }

            item.textContent = text;
            Object.assign(item.style, {
              display: "block",
              clear: "both",
              margin: "5px 7px 2px",
              color: isError ? "#d14343" : "#6b7280",
              fontSize: "12px",
              lineHeight: "1.45",
              textAlign: "left"
            });
          }

          function clearStatus(bubble) {
            bubble.querySelector(
              ":scope .mcm-wa-translation-status")
              ?.remove();
          }

          function legacyCopy(text) {
            const area = document.createElement("textarea");
            area.value = text;
            area.setAttribute("readonly", "");
            Object.assign(area.style, {
              position: "fixed",
              opacity: "0",
              pointerEvents: "none"
            });

            document.body.appendChild(area);
            area.select();
            let copied = false;
            try {
              copied = document.execCommand("copy");
            } finally {
              area.remove();
            }

            if (!copied) {
              throw new Error("Clipboard copy was rejected.");
            }
          }

          function copyText(text) {
            if (navigator.clipboard?.writeText) {
              return navigator.clipboard.writeText(text)
                .catch(() => legacyCopy(text));
            }

            return Promise.resolve()
              .then(() => legacyCopy(text));
          }

          function copyButton(translation, text) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "mcm-wa-translate-copy";
            button.textContent = "复制";
            button.title = "复制译文";
            button.setAttribute("aria-label", "复制译文");

            Object.assign(button.style, {
              position: "absolute",
              top: "5px",
              right: "0",
              minWidth: "42px",
              height: "22px",
              padding: "0 6px",
              border: "1px solid rgba(22,131,232,.45)",
              borderRadius: "11px",
              background: "#fff",
              color: "#1683e8",
              fontSize: "12px",
              fontWeight: "600",
              lineHeight: "1",
              cursor: "pointer"
            });

            button.addEventListener("click", event => {
              event.preventDefault();
              event.stopPropagation();

              copyText(text).then(() => {
                button.textContent = "已复制";
                button.title = "译文已复制";
                setTimeout(() => {
                  if (button.isConnected) {
                    button.textContent = "复制";
                    button.title = "复制译文";
                  }
                }, 1400);
              }).catch(() => {
                button.textContent = "失败";
                button.title = "无法访问剪贴板";
                setTimeout(() => {
                  if (button.isConnected) {
                    button.textContent = "复制";
                    button.title = "复制译文";
                  }
                }, 1400);
              });
            });

            translation.appendChild(button);
            return button;
          }

          function setBusy(bubble, value) {
            const retry = bubble.querySelector(
              ":scope .mcm-wa-translate-retry");
            if (retry) {
              retry.disabled = value;
              retry.style.opacity = value ? ".55" : "1";
            }
          }

          function retryButton(bubble, id) {
            let button = bubble.querySelector(
              ":scope .mcm-wa-translate-retry");

            if (button) return button;

            button = document.createElement("button");
            button.type = "button";
            button.className = "mcm-wa-translate-retry";
            button.textContent = "↻";
            button.title = "重新翻译";
            button.setAttribute("aria-label", "重新翻译");

            Object.assign(button.style, {
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center",
              float: "right",
              width: "22px",
              height: "22px",
              margin: "4px 7px 5px",
              padding: "0",
              border: "1px solid rgba(22,131,232,.45)",
              borderRadius: "11px",
              background: "#fff",
              color: "#1683e8",
              fontSize: "16px",
              fontWeight: "700",
              lineHeight: "1",
              cursor: "pointer"
            });

            button.addEventListener("click", event => {
              event.preventDefault();
              event.stopPropagation();

              const text = sourceFor(bubble);
              if (!hasJapanese(text)) {
                status(bubble, "没有找到可翻译的日文内容。", true);
                return;
              }

              bubble.querySelector(
                ":scope .mcm-wa-translation")
                ?.remove();

              bubble.removeAttribute("data-mcm-wa-translated");
              request(bubble, text, true);
            });

            bubble.appendChild(button);
            return button;
          }

          function request(bubble, text, forceRefresh) {
            const id = idFor(bubble);

            if (pending.has(id)) return;
            if (!forceRefresh &&
                bubble.getAttribute("data-mcm-wa-failed") === "true") {
              return;
            }

            pending.add(id);
            retryButton(bubble, id);
            setBusy(bubble, true);

            status(
              bubble,
              forceRefresh ? "正在重新翻译…" : "正在翻译…",
              false);

            chrome.webview.postMessage(JSON.stringify({
              type: "whatsAppTranslationRequest",
              messageId: id,
              text: text,
              forceRefresh: !!forceRefresh
            }));
          }

          window.__mcmWhatsAppApplyTranslation = (id, text) => {
            const bubble = findBubble(id);
            if (!bubble) return;

            pending.delete(id);
            clearStatus(bubble);
            bubble.querySelector(":scope .mcm-wa-translation")
              ?.remove();

            rememberSource(bubble);

            const translation = document.createElement("div");
            translation.className = "mcm-wa-translation";
            translation.textContent = "中文：" + text;

            Object.assign(translation.style, {
              display: "block",
              position: "relative",
              clear: "both",
              boxSizing: "border-box",
              margin: "6px 7px 2px",
              padding: "6px 50px 0 0",
              borderTop: "1px solid rgba(22,131,232,.30)",
              color: "#1683e8",
              fontSize: "12px",
              fontWeight: "400",
              lineHeight: "1.5",
              textAlign: "left",
              whiteSpace: "pre-wrap",
              wordBreak: "break-word",
              overflowWrap: "anywhere",
              userSelect: "text"
            });

            copyButton(translation, text);

            const retry = retryButton(bubble, id);
            bubble.insertBefore(translation, retry);
            applyPresentation(bubble, translation);
            setBusy(bubble, false);
            bubble.removeAttribute("data-mcm-wa-failed");
            bubble.setAttribute("data-mcm-wa-translated", "true");
          };

          window.__mcmWhatsAppTranslationFailed = (id, text) => {
            const bubble = findBubble(id);
            if (!bubble) return;

            pending.delete(id);
            bubble.setAttribute("data-mcm-wa-failed", "true");
            setSourceVisibility(bubble, "BelowOriginal");
            retryButton(bubble, id);
            setBusy(bubble, false);
            status(bubble, text || "翻译失败，请点击 ↻ 重试。", true);
          };

          window.__mcmUpdateWhatsAppTranslationSettings = value => {
            presentation = normalizePresentation(value);
            document.querySelectorAll(".mcm-wa-translation")
              .forEach(translation => {
                const bubble = translation.parentElement;
                if (bubble) applyPresentation(bubble, translation);
              });
          };

          function scan() {
            scanTimer = null;

            /*
             * WhatsApp 会经常调整正文的 span class；不能只依赖
             * selectable-text。data-testid=msg-container 和
             * data-pre-plain-text 是消息本身的稳定标记，因此先直接
             * 收集消息容器，再读取其中的文字。
             */
            const candidates = new Set();

            document.querySelectorAll(
              "[data-testid='msg-container']")
              .forEach(item => candidates.add(item));

            document.querySelectorAll(
              "[data-pre-plain-text]")
              .forEach(item => {
                const bubble = bubbleFor(item);
                if (bubble) candidates.add(bubble);
              });

            /* 部分 WhatsApp 版本既没有上述 wrapper，也会在消息正文
             * 使用 dir=auto；这个分支只接受带 data-id 的祖先，避免
             * 把左侧聊天列表和搜索结果当作消息。 */
            document.querySelectorAll("[data-id] [dir='auto']")
              .forEach(item => {
                const bubble = bubbleFor(item);
                if (bubble) candidates.add(bubble);
              });

            for (const bubble of candidates) {
              if (!isVisible(bubble) || isOwnUi(bubble) ||
                  bubble.getAttribute("data-mcm-wa-translated") === "true") {
                continue;
              }

              const source = sourceFor(bubble);
              if (hasJapanese(source)) {
                request(bubble, source, false);
              }
            }
          }

          function scheduleScan() {
            if (scanTimer) return;
            scanTimer = setTimeout(scan, 450);
          }

          window.__mcmWhatsAppScan = scheduleScan;

          /* WebView2 的 DocumentCreated 时机早于 html 根节点建立。
           * 监听 Document 本身可避免 document.documentElement 为 null
           * 时脚本中断，后续再由 MutationObserver 扫描真实消息。 */
          new MutationObserver(scheduleScan)
            .observe(document, {
              childList: true,
              subtree: true,
              characterData: true
            });

          scheduleScan();
          setTimeout(scheduleScan, 1200);
          setTimeout(scheduleScan, 2600);
        })();
        """;
    }
}
