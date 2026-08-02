using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MultiChatManager2
{
    public static class UpdateDownloader
    {
        public static async Task<string> DownloadAsync(
            string downloadUrl,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new ArgumentException(
                    "更新下载地址为空。",
                    nameof(downloadUrl));
            }

            string updateFolder = Path.Combine(
                Path.GetTempPath(),
                "LcyLineTranslate",
                "Updates");

            Directory.CreateDirectory(updateFolder);

            string zipPath = Path.Combine(
                updateFolder,
                "LineTranslate_Update.zip");

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using HttpClient client = new()
            {
                Timeout = TimeSpan.FromMinutes(30)
            };

            using HttpResponseMessage response =
                await client.GetAsync(
                    downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            long? totalBytes =
                response.Content.Headers.ContentLength;

            await using Stream sourceStream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            await using FileStream targetStream =
                new(
                    zipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

            byte[] buffer = new byte[81920];
            long totalRead = 0;

            while (true)
            {
                int bytesRead =
                    await sourceStream.ReadAsync(
                        buffer,
                        cancellationToken);

                if (bytesRead == 0)
                {
                    break;
                }

                await targetStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);

                totalRead += bytesRead;

                if (totalBytes.HasValue &&
                    totalBytes.Value > 0)
                {
                    double percent =
                        totalRead * 100d /
                        totalBytes.Value;

                    progress?.Report(percent);
                }
            }

            progress?.Report(100);

            return zipPath;
        }
    }
}