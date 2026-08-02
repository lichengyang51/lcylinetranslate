using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MultiChatManager2
{
    public partial class SetLockPasswordWindow : Window
    {
        private readonly string _settingsFilePath;

        private LockSettings? _existingSettings;

        private bool HasExistingPassword =>
            _existingSettings != null &&
            !string.IsNullOrWhiteSpace(
                _existingSettings.PasswordHash);

        public SetLockPasswordWindow(
            string settingsFilePath)
        {
            InitializeComponent();

            _settingsFilePath =
                settingsFilePath;

            Loaded +=
                SetLockPasswordWindow_Loaded;
        }

        private void SetLockPasswordWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadExistingSettings();

            if (HasExistingPassword)
            {
                PasswordHintTextBlock.Text =
                    "修改密码前，请先输入原来的锁定密码。";

                OldPasswordBox.Focus();
            }
            else
            {
                PasswordHintTextBlock.Text =
                    "首次设置时，旧密码可以留空。";

                NewPasswordBox.Focus();
            }
        }

        private void LoadExistingSettings()
        {
            try
            {
                if (!File.Exists(
                        _settingsFilePath))
                {
                    _existingSettings =
                        null;

                    return;
                }

                string json =
                    File.ReadAllText(
                        _settingsFilePath,
                        Encoding.UTF8);

                _existingSettings =
                    JsonSerializer.Deserialize<LockSettings>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });
            }
            catch
            {
                _existingSettings =
                    null;
            }
        }

        private void ConfirmButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string oldPassword =
                OldPasswordBox.Password;

            string newPassword =
                NewPasswordBox.Password;

            string confirmPassword =
                ConfirmPasswordBox.Password;

            if (HasExistingPassword)
            {
                if (string.IsNullOrWhiteSpace(
                        oldPassword))
                {
                    MessageBox.Show(
                        "请输入旧密码。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    OldPasswordBox.Focus();

                    return;
                }

                if (!VerifyPassword(
                        oldPassword,
                        _existingSettings!.PasswordHash))
                {
                    MessageBox.Show(
                        "旧密码错误，请重新输入。",
                        "密码错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    OldPasswordBox.Clear();
                    OldPasswordBox.Focus();

                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(
                    newPassword))
            {
                MessageBox.Show(
                    "请输入新密码。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                NewPasswordBox.Focus();

                return;
            }

            if (newPassword.Length < 4)
            {
                MessageBox.Show(
                    "新密码不能少于 4 位。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                NewPasswordBox.Focus();

                return;
            }

            if (!string.Equals(
                    newPassword,
                    confirmPassword,
                    StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "两次输入的新密码不一致。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ConfirmPasswordBox.Clear();
                ConfirmPasswordBox.Focus();

                return;
            }

            if (HasExistingPassword &&
                VerifyPassword(
                    newPassword,
                    _existingSettings!.PasswordHash))
            {
                MessageBox.Show(
                    "新密码不能与旧密码相同。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                NewPasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                NewPasswordBox.Focus();

                return;
            }

            try
            {
                string? folder =
                    Path.GetDirectoryName(
                        _settingsFilePath);

                if (!string.IsNullOrWhiteSpace(
                        folder))
                {
                    Directory.CreateDirectory(
                        folder);
                }

                LockSettings settings =
                    new LockSettings
                    {
                        PasswordHash =
                            CreatePasswordHash(
                                newPassword),

                        UpdatedAt =
                            DateTimeOffset.UtcNow
                                .ToString("O")
                    };

                string json =
                    JsonSerializer.Serialize(
                        settings,
                        new JsonSerializerOptions
                        {
                            WriteIndented =
                                true
                        });

                File.WriteAllText(
                    _settingsFilePath,
                    json,
                    Encoding.UTF8);

                MessageBox.Show(
                    HasExistingPassword
                        ? "锁定密码修改成功。"
                        : "锁定密码设置成功。",
                    "设置成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult =
                    true;

                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "保存锁定密码失败：\n\n" +
                    exception.Message,
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool VerifyPassword(
            string password,
            string savedPasswordHash)
        {
            if (string.IsNullOrWhiteSpace(
                    savedPasswordHash))
            {
                return false;
            }

            string enteredPasswordHash =
                CreatePasswordHash(
                    password);

            try
            {
                byte[] enteredHashBytes =
                    Convert.FromHexString(
                        enteredPasswordHash);

                byte[] savedHashBytes =
                    Convert.FromHexString(
                        savedPasswordHash);

                if (enteredHashBytes.Length !=
                    savedHashBytes.Length)
                {
                    return false;
                }

                return CryptographicOperations
                    .FixedTimeEquals(
                        enteredHashBytes,
                        savedHashBytes);
            }
            catch
            {
                return false;
            }
        }

        private static string CreatePasswordHash(
            string password)
        {
            byte[] passwordBytes =
                Encoding.UTF8.GetBytes(
                    password);

            byte[] hashBytes =
                SHA256.HashData(
                    passwordBytes);

            return Convert.ToHexString(
                hashBytes);
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                false;

            Close();
        }
    }

    public sealed class LockSettings
    {
        public string PasswordHash { get; set; } =
            string.Empty;

        public string UpdatedAt { get; set; } =
            string.Empty;

        public int FailedAttempts { get; set; }

        public string? LockoutUntil { get; set; }
    }
}