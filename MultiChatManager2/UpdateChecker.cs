using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    public class UpdateInfo
    {
        public string version { get; set; } = "";
        public string downloadUrl { get; set; } = "";
        public string[] releaseNotes { get; set; } = [];
    }

    public static class UpdateChecker
    {
        // 下一步我们会替换成你的 Supabase 地址
        private const string VersionUrl =
            "https://iyadtuwabsmiohkyfvqv.supabase.co/storage/v1/object/public/updates/version.json";

        public static async Task<UpdateInfo?> CheckAsync()
        {
            using HttpClient client = new();

            var json = await client.GetStringAsync(VersionUrl);

            return JsonSerializer.Deserialize<UpdateInfo>(json);
        }
    }
}