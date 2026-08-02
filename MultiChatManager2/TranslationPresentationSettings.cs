using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MultiChatManager2
{
    /// <summary>
    /// LINE 和 WhatsApp 共用的译文外观设置。
    /// </summary>
    public sealed class TranslationPresentationSettings
    {
        public const string BelowOriginal =
            "BelowOriginal";

        public const string TranslationOnly =
            "TranslationOnly";

        public double FontSize { get; set; } =
            12;

        public string DisplayMode { get; set; } =
            BelowOriginal;

        public void Normalize()
        {
            FontSize = FontSize switch
            {
                <= 13 => 12,
                <= 15 => 14,
                <= 17 => 16,
                _ => 18
            };

            if (DisplayMode != TranslationOnly)
            {
                DisplayMode = BelowOriginal;
            }
        }
    }

    public static class TranslationPresentationSettingsStore
    {
        public static TranslationPresentationSettings Load(
            string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new TranslationPresentationSettings();
                }

                TranslationPresentationSettings? settings =
                    JsonSerializer.Deserialize<TranslationPresentationSettings>(
                        File.ReadAllText(path, Encoding.UTF8),
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                settings ??= new TranslationPresentationSettings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                return new TranslationPresentationSettings();
            }
        }

        public static void Save(
            string path,
            TranslationPresentationSettings settings)
        {
            settings.Normalize();

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(path, json, Encoding.UTF8);
        }
    }
}
