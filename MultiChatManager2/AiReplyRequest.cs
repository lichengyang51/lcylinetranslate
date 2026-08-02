using System.Collections.Generic;
using Microsoft.Web.WebView2.Wpf;

namespace MultiChatManager2
{
    public sealed record AiReplyRequest(
        WebView2 WebView,
        string AccountId,
        string Platform,
        string OriginalText,
        string TranslationText);

    public sealed record AiReplyOptions(
        string GoalCategory,
        IReadOnlyList<string> SpecificGoals,
        string Relationship,
        string ReplyStyle,
        string EmotionExpression,
        string ReplyScope,
        string Pace);
}
