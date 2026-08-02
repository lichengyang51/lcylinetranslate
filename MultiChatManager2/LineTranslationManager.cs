using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    public sealed class LineTranslationManager
    {
        private readonly YoudaoTranslator _translator;
        private readonly TranslationPresentationSettings
            _presentationSettings;
        private readonly ConcurrentDictionary<string, byte> _processing =
            new(StringComparer.Ordinal);

        public LineTranslationManager(
            YoudaoTranslator translator,
            TranslationPresentationSettings presentationSettings)
        {
            _translator = translator;
            _presentationSettings = presentationSettings;
        }

        public void ClearTranslationCache() =>
            _translator.ClearTranslationCache();

        public async Task AttachAsync(WebView2 webView, string accountId)
        {
            webView.CoreWebView2.WebMessageReceived += async (_, e) =>
                await HandleAsync(webView, accountId, e);
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                GetLineTranslationScript());
            webView.CoreWebView2.NavigationCompleted += async (_, __) =>
            {
                try
                {
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        GetLineTranslationScript());
                }
                catch
                {
                }
            };
        }

        private async Task HandleAsync(
            WebView2 webView,
            string accountId,
            CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = string.Empty;
            bool forced = false;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(
                    e.TryGetWebMessageAsString());
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("type", out JsonElement type) ||
                    type.GetString() != "lineTranslationRequest" ||
                    !root.TryGetProperty("messageId", out JsonElement idValue) ||
                    !root.TryGetProperty("text", out JsonElement textValue))
                    return;

                id = idValue.GetString() ?? string.Empty;
                string text = textValue.GetString() ?? string.Empty;
                forced = root.TryGetProperty(
                    "forceRefresh", out JsonElement refresh) &&
                    refresh.ValueKind == JsonValueKind.True;
                if (string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(text))
                    return;

                // 手动点击 ↻ 不受“关闭自动翻译”开关限制。
                if (MainWindow.TranslateVisibleOnly && !forced)
                    return;

                string key = accountId + ":" + id;
                if (!_processing.TryAdd(key, 0))
                {
                    await ReportFailureAsync(webView, id, "这条消息正在翻译，请稍候再试。", forced);
                    return;
                }

                try
                {
                    IProgress<TranslationProgress> progress =
                        new Progress<TranslationProgress>(
                            value =>
                            {
                                _ = ExecuteAsync(
                                    webView,
                                    "(()=>{window.__mcmTranslationProgress?.(" +
                                    JsonSerializer.Serialize(id) +
                                    "," +
                                    value.CompletedParts +
                                    "," +
                                    value.TotalParts +
                                    ");})();");
                            });

                    string? result = await _translator
                        .TranslateJapaneseToChineseAsync(
                            text,
                            forceRefresh: forced,
                            progress: progress);
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        await ReportFailureAsync(webView, id, "没有得到译文，请点击 ↻ 重试。", forced);
                        return;
                    }

                    await ExecuteAsync(webView,
                        "(()=>{window.__mcmApplyTranslation?.(" +
                        JsonSerializer.Serialize(id) + "," +
                        JsonSerializer.Serialize(result) + ");})();");
                }
                finally
                {
                    _processing.TryRemove(key, out _);
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(id))
                    await ReportFailureAsync(webView, id, "翻译失败，请点击 ↻ 重试。", forced);
            }
        }

        private static async Task ReportFailureAsync(
            WebView2 webView,
            string id,
            string message,
            bool wasManual)
        {
            try
            {
                await ExecuteAsync(webView,
                    "(()=>{window.__mcmTranslationFailed?.(" +
                                    JsonSerializer.Serialize(id) + "," +
                                    JsonSerializer.Serialize(message) + "," +
                                    (wasManual ? "true" : "false") + ");})();");
            }
            catch
            {
            }
        }

        private static async Task ExecuteAsync(WebView2 view, string script)
        {
            await view.Dispatcher.InvokeAsync(async () =>
            {
                if (view.CoreWebView2 is not null)
                    await view.CoreWebView2.ExecuteScriptAsync(script);
            });
        }

        private string GetLineTranslationScript() =>
            $$$$$$$$$$"""
            (()=> {
              const initialPresentation={{{{{{{{{{JsonSerializer.Serialize(_presentationSettings)}}}}}}}}}};
              if(window.__mcmLineTranslatorInstalled) {
                window.__mcmUpdateLineTranslationSettings?.(initialPresentation);
                window.__mcmScanLineMessages?.(); return;
              }
              window.__mcmLineTranslatorInstalled=true;
              const bubbles=new Map(), pending=new Set();
              let number=1, timer=null;
              let presentation=normalizePresentation(initialPresentation);
              const clean=t=>String(t||"").replace(/\u200B/g,"").replace(/\r/g,"").replace(/[ \t]+/g," ").replace(/\n{3,}/g,"\n\n").trim();
              const ja=t=>/[\u3040-\u30ff\u31f0-\u31ff]/.test(t);
              function normalizePresentation(value) {
                const fontSize=Math.min(18,Math.max(12,Number(value?.FontSize)||12));
                return {fontSize,displayMode:value?.DisplayMode==="TranslationOnly"?"TranslationOnly":"BelowOriginal"};
              }
              const excluded=e=>!e||!!e.closest("input,textarea,button,nav,header,footer,[contenteditable='true'],.mcm-line-translation,.mcm-line-translate-copy,.mcm-line-translate-retry,.mcm-line-translate-full,.mcm-line-translation-status");
              function visible(e) {
                if(!e||!(e instanceof HTMLElement)) return false;
                const s=getComputedStyle(e),r=e.getBoundingClientRect();
                return s.display!=="none"&&s.visibility!=="hidden"&&Number(s.opacity)!==0&&r.width>0&&r.height>0;
              }
              function bubbleFor(e) {
                const source=clean(e.innerText); let current=e;
                for(let i=0;i<9&&current;i++,current=current.parentElement) {
                  if(!(current instanceof HTMLElement)) break;
                  const r=current.getBoundingClientRect(),s=getComputedStyle(current);
                  const valid=r.left>=innerWidth*.28&&r.top>=100&&r.bottom<=innerHeight-45&&r.width>=35&&r.width<=innerWidth*.70&&r.height>=20&&r.height<=30000&&(parseFloat(s.borderRadius)||0)>=5&&s.backgroundColor!=="transparent"&&s.backgroundColor!=="rgba(0, 0, 0, 0)";
                  const now=clean(current.innerText);
                  if(valid&&(now===source||now.startsWith(source))) return current;
                } return null;
              }
              function idFor(b) {
                let id=b.getAttribute("data-mcm-message-id");
                if(!id) { id="mcm-"+Date.now()+"-"+number++; b.setAttribute("data-mcm-message-id",id); }
                bubbles.set(id,b); return id;
              }
              function find(id) {
                return bubbles.get(id)||document.querySelector('[data-mcm-message-id="'+id+'"]');
              }
              function sourceFor(b) {
                const remembered=b.getAttribute("data-mcm-line-source");
                if(remembered) return remembered;
                const c=b.cloneNode(true);
                c.querySelectorAll(".mcm-line-translation,.mcm-line-translate-copy,.mcm-line-translate-retry,.mcm-line-translate-full,.mcm-line-translation-status").forEach(x=>x.remove());
                // “显示更多”只是 LINE 的折叠控件，不能送去翻译。
                Array.from(c.querySelectorAll("*")).filter(x=>clean(x.innerText)==="显示更多").forEach(x=>x.remove());
                /*
                 * LINE 的“回复”会把被回复的消息作为一个子卡片放在
                 * 新消息上方。该卡片也可能有日文；不能把它和新消息
                 * 一起交给翻译接口。优先取最下面的日文内容块。
                 */
                const directParts=Array.from(c.children)
                  .map(x=>clean(x.innerText))
                  .filter(x=>ja(x));
                if(directParts.length>1) {
                  return directParts[directParts.length-1];
                }
                return clean(c.innerText);
              }
              /*
               * 超长消息的正文常被 LINE 拆成很多直接子节点；普通
               * 自动翻译仍使用 sourceFor（用于避开“回复引用”），但
               * 全文翻译必须取整条展开后的消息，不能只取最后一段。
               */
              function fullSourceFor(b) {
                const remembered=b.getAttribute("data-mcm-line-source");
                if(remembered) return remembered;
                const c=b.cloneNode(true);
                c.querySelectorAll(".mcm-line-translation,.mcm-line-translate-copy,.mcm-line-translate-retry,.mcm-line-translate-full,.mcm-line-translation-status").forEach(x=>x.remove());
                Array.from(c.querySelectorAll("*")).filter(x=>clean(x.innerText)==="显示更多").forEach(x=>x.remove());
                return clean(c.innerText);
              }
              function rememberSource(b) {
                const source=fullSourceFor(b);
                if(source) b.setAttribute("data-mcm-line-source",source);
              }
              function isOwnDirectChild(child) {
                return child.classList.contains("mcm-line-translation")||
                  child.classList.contains("mcm-line-translate-copy")||
                  child.classList.contains("mcm-line-translate-retry")||
                  child.classList.contains("mcm-line-translate-full")||
                  child.classList.contains("mcm-line-translation-status");
              }
              function setSourceVisibility(b,displayMode=presentation.displayMode) {
                const hide=displayMode==="TranslationOnly";
                Array.from(b.children).forEach(child=>{
                  if(!(child instanceof HTMLElement)||isOwnDirectChild(child)) return;
                  if(hide) {
                    if(!child.hasAttribute("data-mcm-line-original-display")) {
                      child.setAttribute("data-mcm-line-original-display",child.style.display);
                    }
                    child.style.display="none";
                  } else if(child.hasAttribute("data-mcm-line-original-display")) {
                    child.style.display=child.getAttribute("data-mcm-line-original-display")||"";
                    child.removeAttribute("data-mcm-line-original-display");
                  }
                });
              }
              function applyPresentation(b,translation) {
                translation.style.fontSize=presentation.fontSize+"px";
                setSourceVisibility(b);
              }
              /*
               * “显示更多”在不同 LINE 版本中，可能是消息气泡的子元素
               * 或同级元素。按屏幕位置关联，避免依赖固定 DOM 结构。
               */
              function moreFor(b) {
                const r=b.getBoundingClientRect();
                return Array.from(document.querySelectorAll("div,span,p,a,button"))
                  .find(e=>{
                    if(!visible(e)||e.classList.contains("mcm-line-translate-full")||clean(e.innerText)!=="显示更多") return false;
                    const q=e.getBoundingClientRect();
                    return q.right>=r.left-32&&q.left<=r.right+32&&q.top>=r.top-24&&q.top<=r.bottom+72;
                  })||null;
              }
              /*
               * 反向从“显示更多”寻找它所属的消息气泡。先前只从日文
               * 文本往上找气泡；在某些 LINE 版面里，折叠控件与正文
               * 是不同节点，导致长消息从未获得“翻译全文”按钮。
               */
              function bubbleFromMore(more) {
                let current=more;
                for(let i=0;i<12&&current;i++,current=current.parentElement) {
                  if(!(current instanceof HTMLElement)) break;
                  const r=current.getBoundingClientRect(),s=getComputedStyle(current);
                  const valid=r.left>=innerWidth*.28&&r.top>=100&&
                    r.width>=35&&r.width<=innerWidth*.76&&r.height>=20&&
                    r.height<=30000&&(parseFloat(s.borderRadius)||0)>=5&&
                    s.backgroundColor!=="transparent"&&s.backgroundColor!=="rgba(0, 0, 0, 0)";
                  if(valid&&ja(clean(current.innerText))) return current;
                }
                return null;
              }
              function status(b,text,bad) {
                let s=b.querySelector(":scope > .mcm-line-translation-status");
                if(!s) {s=document.createElement("div");s.className="mcm-line-translation-status";b.appendChild(s);}
                s.textContent=text;
                Object.assign(s.style,{display:"block",margin:"6px 0 0",padding:"5px 0 0",borderTop:"1px solid rgba(22,131,232,.25)",fontSize:"12px",lineHeight:"1.45",color:bad?"#d14343":"#6b7280"});
              }
              function clearStatus(b) {b.querySelector(":scope > .mcm-line-translation-status")?.remove();}
              function legacyCopy(text) {
                const area=document.createElement("textarea");
                area.value=text;area.setAttribute("readonly","");
                Object.assign(area.style,{position:"fixed",opacity:"0",pointerEvents:"none"});
                document.body.appendChild(area);area.select();
                let copied=false;
                try {copied=document.execCommand("copy");} finally {area.remove();}
                if(!copied) throw new Error("Clipboard copy was rejected.");
              }
              function copyText(text) {
                if(navigator.clipboard?.writeText) {
                  return navigator.clipboard.writeText(text).catch(()=>legacyCopy(text));
                }
                return Promise.resolve().then(()=>legacyCopy(text));
              }
              function copyButton(translation,text) {
                const x=document.createElement("button");x.type="button";x.className="mcm-line-translate-copy";
                x.textContent="复制";x.title="复制译文";x.setAttribute("aria-label","复制译文");
                Object.assign(x.style,{position:"absolute",top:"5px",right:"0",minWidth:"42px",height:"22px",padding:"0 6px",border:"1px solid rgba(22,131,232,.45)",borderRadius:"11px",background:"#fff",color:"#1683e8",fontSize:"12px",fontWeight:"600",lineHeight:"1",cursor:"pointer"});
                x.addEventListener("click",ev=>{
                  ev.preventDefault();ev.stopPropagation();
                  copyText(text).then(()=>{
                    x.textContent="已复制";x.title="译文已复制";
                    setTimeout(()=>{if(x.isConnected){x.textContent="复制";x.title="复制译文";}},1400);
                  }).catch(()=>{
                    x.textContent="失败";x.title="无法访问剪贴板";
                    setTimeout(()=>{if(x.isConnected){x.textContent="复制";x.title="复制译文";}},1400);
                  });
                });
                translation.appendChild(x);return x;
              }
              function setBusy(b,yes) {
                const x=b.querySelector(":scope > .mcm-line-translate-retry");
                if(x) {x.disabled=yes;x.style.opacity=yes?".55":"1";x.title=yes?"正在翻译":"重新翻译";}
              }
              function retry(b,id) {
                let x=b.querySelector(":scope > .mcm-line-translate-retry");
                if(x) return x;
                x=document.createElement("button");x.type="button";x.className="mcm-line-translate-retry";x.textContent="↻";x.title="重新翻译";x.setAttribute("aria-label","重新翻译");
                Object.assign(x.style,{display:"inline-flex",alignItems:"center",justifyContent:"center",width:"22px",height:"22px",margin:"6px 0 0 auto",padding:"0",border:"1px solid rgba(22,131,232,.45)",borderRadius:"11px",background:"#fff",color:"#1683e8",fontSize:"16px",fontWeight:"700",lineHeight:"1",cursor:"pointer"});
                x.addEventListener("click",ev=>{
                  ev.preventDefault();ev.stopPropagation();
                  const text=sourceFor(b);
                  if(!ja(text)) {status(b,"没有找到可重新翻译的日文内容。",true);return;}
                  if(pending.has(id)) {status(b,"正在翻译，请稍候…",false);return;}
                  b.querySelector(":scope > .mcm-line-translation")?.remove();
                  b.removeAttribute("data-mcm-translated");pending.delete(id);
                  b.removeAttribute("data-mcm-auto-failed");
                  request(b,text,true);
                }); b.appendChild(x); return x;
              }
              function full(b,id,source,knownMore=null) {
                let x=b.querySelector(":scope > .mcm-line-translate-full");
                if(x) return x;
                x=document.createElement("button");x.type="button";x.className="mcm-line-translate-full";
                x.textContent=source.length>1800
                  ? "翻译全文（约"+Math.ceil(source.length/1600)+"段）"
                  : "翻译全文";
                x.title="这条消息较长，点击后分段翻译全文";
                Object.assign(x.style,{display:"inline-flex",alignItems:"center",justifyContent:"center",minHeight:"26px",margin:"8px 0 0 auto",padding:"3px 10px",border:"1px solid rgba(22,131,232,.55)",borderRadius:"13px",background:"#fff",color:"#1683e8",fontSize:"12px",fontWeight:"600",lineHeight:"1.2",cursor:"pointer"});
                x.addEventListener("click",ev=>{
                  ev.preventDefault();ev.stopPropagation();
                  /*
                   * LINE 的超长消息通常先折叠为“显示更多”。
                   * 先点击展开，再读取一次完整内容，避免只翻译首段。
                   */
                  const more=moreFor(b)||knownMore;
                  if(more) {
                    x.disabled=true;x.style.opacity=".55";
                    status(b,"正在展开全文…",false);
                    more.dispatchEvent(new MouseEvent("click",{bubbles:true,cancelable:true}));
                    setTimeout(()=>{
                      const text=fullSourceFor(b);
                      if(!ja(text)) {status(b,"没有找到可翻译的日文内容。",true);x.disabled=false;x.style.opacity="1";return;}
                      x.remove();request(b,text,true,true);
                    },700);
                    return;
                  }
                  const text=fullSourceFor(b);
                  if(!ja(text)) {status(b,"没有找到可翻译的日文内容。",true);return;}
                  x.remove();request(b,text,true,true);
                });
                b.appendChild(x);return x;
              }
              /*
               * 不等常规扫描找到日文叶子节点：只要页面出现 LINE 的
               * “显示更多”，就直接在它对应的气泡中创建全文翻译按钮。
               */
              function addFullButtonsForCollapsedMessages() {
                Array.from(document.querySelectorAll("div,span,p,a,button"))
                  .filter(e=>visible(e)&&clean(e.innerText)==="显示更多")
                  .forEach(more=>{
                    const b=bubbleFromMore(more);
                    if(!b||b.getAttribute("data-mcm-translated")==="true") return;
                    const id=idFor(b);
                    if(pending.has(id)) return;
                    full(b,id,fullSourceFor(b),more);
                  });
              }
              function request(b,text,forced=false,fullMessage=false) {
                if(b.getAttribute("data-mcm-translated")==="true"&&!forced)return;
                const id=idFor(b);
                if(pending.has(id)) {status(b,"正在翻译，请稍候…",false);return;}
                if(!forced&&b.getAttribute("data-mcm-auto-failed")==="true")return;
                pending.add(id);b.querySelector(":scope > .mcm-line-translate-full")?.remove();retry(b,id);setBusy(b,true);
                if(forced)status(b,fullMessage?"正在翻译全文…":"正在重新翻译…",false);
                chrome.webview.postMessage(JSON.stringify({type:"lineTranslationRequest",messageId:id,text,forceRefresh:forced}));
              }
              window.__mcmApplyTranslation=(id,text)=>{
                const b=find(id);if(!b)return;pending.delete(id);clearStatus(b);
                b.querySelector(":scope > .mcm-line-translate-full")?.remove();
                b.querySelector(":scope > .mcm-line-translation")?.remove();
                rememberSource(b);
                const x=retry(b,id),t=document.createElement("div");t.className="mcm-line-translation";t.textContent=text;
                Object.assign(t.style,{display:"block",position:"relative",float:"none",clear:"both",width:"100%",maxWidth:"100%",boxSizing:"border-box",margin:"6px 0 0",padding:"6px 50px 0 0",borderTop:"1px solid rgba(22,131,232,.30)",color:"#1683e8",fontSize:"12px",fontWeight:"400",lineHeight:"1.5",textAlign:"left",whiteSpace:"pre-wrap",wordBreak:"break-word",overflowWrap:"anywhere",userSelect:"text"});
                copyButton(t,text);b.insertBefore(t,x);applyPresentation(b,t);setBusy(b,false);b.removeAttribute("data-mcm-auto-failed");b.setAttribute("data-mcm-translated","true");
              };
              window.__mcmTranslationProgress=(id,completed,total)=>{
                const b=find(id);if(!b)return;
                const current=Math.max(1,Math.min(Number(total)||1,Number(completed)||1));
                const count=Math.max(1,Number(total)||1);
                status(b,"正在翻译全文 "+current+"/"+count+" 段…",false);
              };
              window.__mcmTranslationFailed=(id,msg,wasManual)=>{
                const b=find(id);if(!b)return;pending.delete(id);b.removeAttribute("data-mcm-translated");
                if(wasManual) b.removeAttribute("data-mcm-auto-failed");
                else b.setAttribute("data-mcm-auto-failed","true");
                setSourceVisibility(b,"BelowOriginal");
                retry(b,id);setBusy(b,false);status(b,msg||"翻译失败，请点击 ↻ 重试。",true);
              };
              window.__mcmUpdateLineTranslationSettings=value=>{
                presentation=normalizePresentation(value);
                document.querySelectorAll(".mcm-line-translation").forEach(t=>{
                  const b=t.parentElement;if(b) applyPresentation(b,t);
                });
              };
              window.__mcmScanLineMessages=()=>{
                addFullButtonsForCollapsedMessages();
                const seen=new Set();
                document.querySelectorAll("div,span,p").forEach(e=>{
                  if(!visible(e)||excluded(e))return;
                  const text=clean(e.innerText);
                  if(!ja(text)||text.length<1||text.length>60000)return;
                  if(Array.from(e.children).some(c=>clean(c.innerText)===text))return;
                  const b=bubbleFor(e);if(!b||seen.has(b))return;seen.add(b);
                  const source=sourceFor(b);if(!ja(source))return;
                  const id=idFor(b);
                  const fullText=fullSourceFor(b);
                  const more=moreFor(b);
                  if((fullText.length>1800||more)&&
                     b.getAttribute("data-mcm-translated")!=="true"&&
                     !pending.has(id)) {
                    full(b,id,fullText,more);return;
                  }
                  request(b,source);
                });
              };
              /*
               * “显示更多”是 LINE 异步插入的节点。这里不能再等很久，
               * 否则用户会以为“翻译全文”按钮没有生成；留出极短时间
               * 给 LINE 完成排版后立即扫描。
               */
              function schedule(){clearTimeout(timer);timer=setTimeout(window.__mcmScanLineMessages,80);}
              function watch(){if(!document.documentElement){setTimeout(watch,100);return;}new MutationObserver(schedule).observe(document.documentElement,{childList:true,subtree:true,characterData:true});schedule();setInterval(window.__mcmScanLineMessages,900);}
              watch();
            })();
            """;
    }
}
