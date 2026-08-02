using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiChatManager2
{
    /// <summary>
    /// AI 智能回复的本地设置。API Key 只会以当前 Windows 用户可解密的
    /// DPAPI 密文写入磁盘，绝不会进入安装包、更新包或远程配置。
    /// </summary>
    public sealed class AiReplySettings
    {
        public const string DefaultModel = "gpt-5.6-luna";

        public string Model { get; set; } = DefaultModel;

        public string ProtectedApiKey { get; set; } = string.Empty;

        [JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey);

        public void Normalize()
        {
            Model = string.IsNullOrWhiteSpace(Model)
                ? DefaultModel
                : Model.Trim();
        }
    }

    public static class AiReplySettingsStore
    {
        public static AiReplySettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new AiReplySettings();
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                AiReplySettings settings = JsonSerializer.Deserialize<AiReplySettings>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new AiReplySettings();

                settings.Normalize();

                if (!string.IsNullOrWhiteSpace(settings.ProtectedApiKey))
                {
                    settings.ApiKey = WindowsDataProtector.Unprotect(
                        settings.ProtectedApiKey);
                }

                return settings;
            }
            catch
            {
                // 设置文件损坏或不是当前 Windows 用户创建时，不让程序崩溃；
                // 用户可以在设置中重新保存自己的 Key。
                return new AiReplySettings();
            }
        }

        public static void Save(string path, AiReplySettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Normalize();

            settings.ProtectedApiKey =
                string.IsNullOrWhiteSpace(settings.ApiKey)
                    ? string.Empty
                    : WindowsDataProtector.Protect(settings.ApiKey.Trim());

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static string CreateSafetyIdentifier()
        {
            string input = Environment.UserName + "|" +
                Environment.MachineName + "|LcyLineTranslate";

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

            return "mcm-" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
        }
    }

    internal static class WindowsDataProtector
    {
        private const int CryptprotectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("Kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static string Protect(string value)
        {
            byte[] input = Encoding.UTF8.GetBytes(value);
            DataBlob inputBlob = CreateBlob(input);
            DataBlob outputBlob = default;

            try
            {
                if (!CryptProtectData(
                        ref inputBlob,
                        "LcyLineTranslate AI Reply API Key",
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptprotectUiForbidden,
                        out outputBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return Convert.ToBase64String(ReadBlob(outputBlob));
            }
            finally
            {
                FreeHGlobal(inputBlob);
                FreeLocal(outputBlob);
            }
        }

        public static string Unprotect(string protectedValue)
        {
            byte[] input = Convert.FromBase64String(protectedValue);
            DataBlob inputBlob = CreateBlob(input);
            DataBlob outputBlob = default;

            try
            {
                if (!CryptUnprotectData(
                        ref inputBlob,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptprotectUiForbidden,
                        out outputBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return Encoding.UTF8.GetString(ReadBlob(outputBlob));
            }
            finally
            {
                FreeHGlobal(inputBlob);
                FreeLocal(outputBlob);
            }
        }

        private static DataBlob CreateBlob(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return default;
            }

            IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);

            return new DataBlob
            {
                cbData = bytes.Length,
                pbData = memory
            };
        }

        private static byte[] ReadBlob(DataBlob blob)
        {
            if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
            {
                return Array.Empty<byte>();
            }

            byte[] bytes = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, bytes, 0, bytes.Length);

            return bytes;
        }

        private static void FreeHGlobal(DataBlob blob)
        {
            if (blob.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blob.pbData);
            }
        }

        private static void FreeLocal(DataBlob blob)
        {
            if (blob.pbData != IntPtr.Zero)
            {
                _ = LocalFree(blob.pbData);
            }
        }
    }
}
