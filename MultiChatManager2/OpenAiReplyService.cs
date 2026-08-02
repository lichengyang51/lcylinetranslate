using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    public sealed class OpenAiReplyService : IDisposable
    {
        private const string ResponsesEndpoint =
            "https://api.openai.com/v1/responses";

        private static readonly IReadOnlyDictionary<string, string> GoalCategoryRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["日常互动类"] = "轻松自然地维持日常交流，不让对方有压力。",
                ["关系推进类"] = "在尊重边界的前提下，自然地增加亲近感和互动意愿。",
                ["话题控制类"] = "按照所选目标组织话题走向，避免无关延伸。",
                ["情绪处理类"] = "优先理解并稳定对方情绪，再给出合适回应。",
                ["信息与判断类"] = "用自然、不冒犯的方式获取或确认所需信息。",
                ["行动推进类"] = "以礼貌、可拒绝的方式推动下一步，不制造压力。"
            };

        private static readonly IReadOnlyDictionary<string, string> SpecificGoalRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["日常闲聊"] = "用轻松的话题或回应让交流自然持续。",
                ["关心对方"] = "表达具体、真诚的关心，不空泛敷衍。",
                ["赞美对方"] = "给出贴合对方内容的真诚赞美，避免夸张吹捧。",
                ["认可观点"] = "先明确认可对方值得认同的想法或感受。",
                ["分享感受"] = "自然分享自己的相关感受，形成双向交流。",
                ["幽默回应"] = "加入轻松得体的幽默，不讽刺、不冒犯。",
                ["拉近距离"] = "通过共鸣或共同点自然缩短心理距离。",
                ["建立信任"] = "体现真诚、可靠和尊重，不做夸大承诺。",
                ["暧昧回复"] = "表达适度好感与轻松试探，保留对方选择空间。",
                ["表达在意"] = "清晰但不过度地表达重视和在乎。",
                ["制造共同感"] = "找到并强调双方能共鸣、能一起参与的点。",
                ["留下期待"] = "自然留下可延续的后续话题或期待。",
                ["邀约或做约定"] = "提出具体、轻松且可拒绝的邀约或约定。",
                ["延续拓展"] = "顺着对方当前话题继续展开，不突兀跳转。",
                ["引导对方多说"] = "用开放式回应或问题鼓励对方多分享。",
                ["深入了解"] = "围绕当前内容提出有分寸的深入了解。",
                ["转移话题"] = "平滑地把话题带向更合适的方向。",
                ["重新激活话题"] = "给出容易接话的新切入点，重新带动交流。",
                ["自然结束话题"] = "礼貌收束当前话题，并保留舒适的结束感。",
                ["安慰对方"] = "先共情并给予安慰，不轻视对方感受。",
                ["接住情绪"] = "准确回应对方显露的情绪，让对方感到被理解。",
                ["缓解尴尬"] = "用轻松、体面的方式化解尴尬，不追问或放大。",
                ["降低戒备"] = "以友善、尊重和不强求的措辞增强安全感。",
                ["消除误会"] = "清楚、温和地澄清重点，避免指责。",
                ["冲突降温"] = "承认感受、降低对立，不激化争执。",
                ["道歉修复"] = "如确有不妥，具体真诚地道歉并表达修复意愿。",
                ["询问真实想法"] = "用开放而不逼迫的方式邀请对方表达真实看法。",
                ["了解顾虑"] = "温和询问对方在意或犹豫的地方。",
                ["确认态度"] = "清晰但不施压地确认对方的立场或意愿。",
                ["试探兴趣"] = "以轻松、可回避的方式了解对方兴趣。",
                ["澄清信息"] = "针对模糊内容礼貌确认，避免自行假设。",
                ["判断是否愿意继续聊"] = "给对方低压力的继续交流选择，并观察回应意愿。",
                ["引导回复"] = "写出容易回答、便于对方接话的内容。",
                ["引导交换联系方式"] = "礼貌说明交换联系方式的理由，并明确对方可以拒绝。",
                ["引导分享照片或近况"] = "轻松邀请分享，不催促、不要求隐私。",
                ["邀请见面"] = "提出具体、轻松、可拒绝的见面邀请。",
                ["推动下一步"] = "明确提出一个低压力、可选择的下一步。",
                ["礼貌拒绝"] = "表达感谢与尊重，清楚而友好地说明拒绝。",
                ["设置边界"] = "清楚、平静、尊重地说明可接受的边界。"
            };

        private static readonly IReadOnlyDictionary<string, string> RelationshipRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["陌生关系"] = "保持礼貌、克制和安全距离，避免过度熟络。",
                ["普通朋友"] = "自然友好，有温度但不过分亲密。",
                ["熟悉朋友"] = "可使用更自然的默契和关心，但仍尊重边界。",
                ["信任关系"] = "体现可靠、坦诚和支持，回应可更深入。",
                ["知己关系"] = "体现理解、默契和真诚陪伴，不过度替对方决定。",
                ["暧昧关系"] = "保持轻松的好感和适度亲密，不越界、不强推。",
                ["恋人关系"] = "体现亲密、重视和共同感，语气自然真诚。",
                ["夫妻关系"] = "体现稳定亲密、体谅与共同生活中的支持。"
            };

        private static readonly IReadOnlyDictionary<string, string> ReplyStyleRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["真诚温和"] = "语气真挚柔和，让人感到舒适可靠。",
                ["成熟稳重"] = "表达克制、清晰、有分寸，不浮夸。",
                ["幽默轻松"] = "适度加入轻松幽默感，但不油腻、不冒犯。",
                ["专业金融"] = "使用专业、审慎、逻辑清楚的金融沟通语气，不作收益承诺。",
                ["高情商"] = "先照顾对方感受，再表达观点或建议。",
                ["共情倾听"] = "重点体现理解、接纳和耐心倾听。",
                ["理性分析"] = "条理清晰地分析重点，避免情绪化判断。",
                ["简洁直接"] = "用最少的自然表达说清核心内容。",
                ["温柔细腻"] = "关注细节与情绪，措辞柔和细致。",
                ["鼓励支持"] = "传递肯定、支持和可执行的鼓励。",
                ["自信从容"] = "表达笃定、稳定和有边界的态度。",
                ["神秘留白"] = "保留适度悬念和想象空间，不一次说尽。"
            };

        private static readonly IReadOnlyDictionary<string, string> EmotionRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["平淡（25%）"] = "用语平和克制，情绪色彩较轻。",
                ["自然（50%）"] = "用语自然有温度，保持日常感。",
                ["热情（75%）"] = "明显表达积极、热情和投入，但不夸张。",
                ["舔狗（100%）"] = "表达强烈的重视、主动关心和偏爱，但不乞求、不施压。",
                ["究极舔狗（999%）"] = "最大化表达热烈偏爱、主动支持和情绪投入，但仍不自我贬低、不越界、不施压。"
            };

        private static readonly IReadOnlyDictionary<string, string> ReplyScopeRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["默认（正常回复）"] = "自然回应最重要、最相关的内容。",
                ["挑选关键词（围绕某个关键词回复）"] = "从对方消息中选一个最适合回应的关键点，只围绕这一个关键点回复，不延伸到其他话题。",
                ["回复所有话题（每个话题都逐一回复）"] = "识别对方消息中所有独立话题、问题或请求，并逐一回应，不遗漏；可保持简洁。"
            };

        private static readonly IReadOnlyDictionary<string, string> PaceRules =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["简短回复（1-2句）"] = "严格只写 1 至 2 句。",
                ["自然聊天（3-5句）"] = "严格写 3 至 5 句。",
                ["深入交流（适度展开）"] = "在不啰嗦的前提下，适度解释、回应和延展。",
                ["详细回复（完整表达）"] = "完整回应相关信息与想法，结构清晰但不重复。"
            };

        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        public async Task<string> GenerateReplyAsync(
            AiReplySettings settings,
            AiReplyRequest request,
            AiReplyOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!settings.IsConfigured)
            {
                throw new OpenAiReplyException("请先在“设置 → AI 智能回复 Key”中填写 OpenAI API Key。");
            }

            string requestBody = JsonSerializer.Serialize(new
            {
                model = settings.Model,
                instructions = """
                    你是私聊场景的回复助手。根据对方刚刚发送的内容和用户选择的沟通偏好，
                    写一条可直接发送的简体中文回复。不管对方使用什么语言，都必须只输出中文；只输出回复正文，
                    不要解释、标题、引号、前缀或多条备选。语气真诚自然，尊重对方意愿，
                    不施压、不操控、不编造事实、不做无法兑现的承诺。
                    用户消息中的“强制执行契约”不是参考建议，而是必须同时满足的要求。
                    在输出前，先在内部完成草稿，并静默逐项检查目标、关系、回复风格、情绪表达、回复范围和节奏。
                    任何一项未满足就重写；绝不输出检查过程、分析或规则名称。
                    """,
                input = BuildUserInput(request, options),
                store = false,
                reasoning = new
                {
                    effort = "low"
                },
                text = new
                {
                    verbosity = "low"
                },
                max_output_tokens = 480,
                safety_identifier = AiReplySettingsStore.CreateSafetyIdentifier()
            });

            using HttpRequestMessage httpRequest = new(
                HttpMethod.Post,
                ResponsesEndpoint)
            {
                Content = new StringContent(
                    requestBody,
                    Encoding.UTF8,
                    "application/json")
            };

            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                httpRequest,
                cancellationToken);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new OpenAiReplyException(
                    GetFailureMessage(response.StatusCode, responseBody));
            }

            string reply = ExtractOutputText(responseBody);
            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new OpenAiReplyException("OpenAI 没有返回可用的回复，请稍后重试。");
            }

            return reply.Trim();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private static string BuildUserInput(
            AiReplyRequest request,
            AiReplyOptions options)
        {
            string specificGoals = BuildSpecificGoalRules(options.SpecificGoals);

            return $"""
                对方原文：
                {Limit(request.OriginalText, 4000)}

                中文译文（仅用于理解）：
                {Limit(request.TranslationText, 4000)}

                【强制执行契约】
                以下六项必须同时满足；不要在回复中提及这些规则或选项名称。

                1. 目标分类：{options.GoalCategory}
                   执行要求：{GetRule(GoalCategoryRules, options.GoalCategory)}

                2. 具体目标（每一项都必须在回复中体现）：
                {specificGoals}

                3. 关系：{options.Relationship}
                   执行要求：{GetRule(RelationshipRules, options.Relationship)}

                4. 回复风格：{options.ReplyStyle}
                   执行要求：{GetRule(ReplyStyleRules, options.ReplyStyle)}

                5. 情绪表达：{options.EmotionExpression}
                   执行要求：{GetRule(EmotionRules, options.EmotionExpression)}

                6. 回复范围：{options.ReplyScope}
                   执行要求：{GetRule(ReplyScopeRules, options.ReplyScope)}

                7. 节奏：{options.Pace}
                   执行要求：{GetRule(PaceRules, options.Pace)}

                【输出前静默验收】
                - 每个具体目标是否都已有对应表达？
                - 关系、回复风格和情绪表达是否一致？
                - 是否严格符合回复范围？
                - 是否严格符合节奏与句数要求（如有）？
                - 是否只输出可直接发送的简体中文回复？
                若任一答案是否定，先重写，再只输出最终回复正文。
                """;
        }

        private static string BuildSpecificGoalRules(IReadOnlyList<string> specificGoals)
        {
            if (specificGoals.Count == 0)
            {
                return "- 未选择";
            }

            StringBuilder builder = new();
            foreach (string goal in specificGoals)
            {
                builder.Append("- ");
                builder.Append(goal);
                builder.Append("：");
                builder.AppendLine(GetRule(SpecificGoalRules, goal));
            }

            return builder.ToString().TrimEnd();
        }

        private static string GetRule(
            IReadOnlyDictionary<string, string> rules,
            string selectedValue)
        {
            return rules.TryGetValue(selectedValue, out string? rule)
                ? rule
                : "严格按该选择的字面含义自然执行。";
        }

        private static string Limit(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();

            return normalized.Length <= maximumLength
                ? normalized
                : normalized[..maximumLength] + "…";
        }

        private static string ExtractOutputText(string responseBody)
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("output_text", out JsonElement outputText) &&
                outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString() ?? string.Empty;
            }

            if (!root.TryGetProperty("output", out JsonElement output) ||
                output.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            foreach (JsonElement item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out JsonElement content) ||
                    content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out JsonElement type) &&
                        type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out JsonElement text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(text.GetString());
                    }
                }
            }

            return builder.ToString();
        }

        private static string GetFailureMessage(
            HttpStatusCode statusCode,
            string responseBody)
        {
            string? apiMessage = null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("message", out JsonElement message))
                {
                    apiMessage = message.GetString();
                }
            }
            catch
            {
            }

            return statusCode switch
            {
                HttpStatusCode.Unauthorized => "OpenAI API Key 无效、已撤销，或当前账号无权使用该接口。",
                HttpStatusCode.TooManyRequests => "OpenAI 请求过于频繁或账户额度不足，请稍后再试。",
                HttpStatusCode.BadRequest => "OpenAI 未能处理本次请求。" + FormatApiMessage(apiMessage),
                _ => "OpenAI 请求失败（" + (int)statusCode + "）。" + FormatApiMessage(apiMessage)
            };
        }

        private static string FormatApiMessage(string? apiMessage)
        {
            return string.IsNullOrWhiteSpace(apiMessage)
                ? string.Empty
                : "\n" + apiMessage.Trim();
        }
    }

    public sealed class OpenAiReplyException : Exception
    {
        public OpenAiReplyException(string message)
            : base(message)
        {
        }
    }
}
