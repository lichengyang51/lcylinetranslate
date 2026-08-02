using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MultiChatManager2
{
    public partial class LockWindow : Window
    {
        private readonly string _lockSettingsPath;

        private readonly DispatcherTimer
            _lockoutTimer;

        private bool _isUnlocked;

        public LockWindow(
            string lockSettingsPath)
        {
            InitializeComponent();

            _lockSettingsPath =
                lockSettingsPath;

            _lockoutTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(1)
                };

            _lockoutTimer.Tick +=
                LockoutTimer_Tick;

            Loaded +=
                LockWindow_Loaded;

            Closing +=
                LockWindow_Closing;
        }

        private void LockWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            ForgotPasswordPanel.Visibility =
                Visibility.Collapsed;

            UnlockPasswordBox.Clear();

            CheckCurrentLockout();

            if (UnlockPasswordBox.IsEnabled)
            {
                UnlockPasswordBox.Focus();
            }
        }

        private void LockWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (_isUnlocked)
            {
                return;
            }

            // 未输入正确密码时，禁止通过 Alt+F4 或右上角关闭。
            e.Cancel =
                true;

            HintTextBlock.Text =
                "软件仍处于锁定状态，请输入正确密码。";
        }

        private void UnlockPasswordBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled =
                true;

            TryUnlock();
        }

        private void ConfirmButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            TryUnlock();
        }

        private void TryUnlock()
        {
            LockSettings? settings =
                LoadLockSettings();

            if (settings == null ||
                string.IsNullOrWhiteSpace(
                    settings.PasswordHash))
            {
                HintTextBlock.Text =
                    "没有找到锁定密码，请点击“忘记密码”重新设置。";

                return;
            }

            if (TryGetLockoutRemaining(
                    settings,
                    out TimeSpan remaining))
            {
                StartLockoutTimer();

                ShowLockoutStatus(
                    remaining);

                return;
            }

            string password =
                UnlockPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(
                    password))
            {
                HintTextBlock.Text =
                    "请输入解锁密码。";

                UnlockPasswordBox.Focus();

                return;
            }

            if (VerifyPassword(
                    password,
                    settings.PasswordHash))
            {
                settings.FailedAttempts =
                    0;

                settings.LockoutUntil =
                    null;

                SaveLockSettings(
                    settings);

                StopLockoutTimer();

                _isUnlocked =
                    true;

                DialogResult =
                    true;

                Close();

                return;
            }

            settings.FailedAttempts++;

            UnlockPasswordBox.Clear();

            if (settings.FailedAttempts >= 5)
            {
                settings.FailedAttempts =
                    5;

                settings.LockoutUntil =
                    DateTimeOffset.UtcNow
                        .AddMinutes(10)
                        .ToString("O");

                SaveLockSettings(
                    settings);

                StartLockoutTimer();

                ShowLockoutStatus(
                    TimeSpan.FromMinutes(10));

                return;
            }

            SaveLockSettings(
                settings);

            int attemptsRemaining =
                5 -
                settings.FailedAttempts;

            HintTextBlock.Text =
                $"密码错误，还可以尝试 {attemptsRemaining} 次。";

            UnlockPasswordBox.Focus();
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            UnlockPasswordBox.Clear();

            HintTextBlock.Text =
                "软件仍处于锁定状态。";

            if (Owner != null)
            {
                Owner.WindowState =
                    WindowState.Minimized;
            }

            WindowState =
                WindowState.Minimized;
        }

        private void ForgotPasswordButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ForgotPasswordPanel.Visibility =
                Visibility.Visible;

            ResetLicenseKeyTextBox.Clear();

            ResetLicenseKeyTextBox.Focus();
        }

        private void CloseForgotPasswordPanel_Click(
            object sender,
            RoutedEventArgs e)
        {
            ForgotPasswordPanel.Visibility =
                Visibility.Collapsed;

            ResetLicenseKeyTextBox.Clear();

            if (UnlockPasswordBox.IsEnabled)
            {
                UnlockPasswordBox.Focus();
            }
        }

        private void ResetPasswordByLicense_Click(
            object sender,
            RoutedEventArgs e)
        {
            string enteredLicenseKey =
                ResetLicenseKeyTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(
                    enteredLicenseKey))
            {
                MessageBox.Show(
                    "请输入当前设备使用的激活码。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ResetLicenseKeyTextBox.Focus();

                return;
            }

            string? savedLicenseKey =
                ReadSavedLicenseKey();

            if (string.IsNullOrWhiteSpace(
                    savedLicenseKey))
            {
                MessageBox.Show(
                    "本机没有找到已保存的激活信息。",
                    "重置失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            if (!string.Equals(
                    enteredLicenseKey,
                    savedLicenseKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "激活码错误，无法重置锁定密码。",
                    "验证失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ResetLicenseKeyTextBox.Clear();

                ResetLicenseKeyTextBox.Focus();

                return;
            }

            MessageBoxResult confirmResult =
                MessageBox.Show(
                    "激活码验证成功。\n\n确定要重新设置锁定密码吗？",
                    "重置锁定密码",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirmResult !=
                MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                StopLockoutTimer();

                if (File.Exists(
                        _lockSettingsPath))
                {
                    File.Delete(
                        _lockSettingsPath);
                }

                SetLockPasswordWindow passwordWindow =
                    new SetLockPasswordWindow(
                        _lockSettingsPath)
                    {
                        Owner =
                            this
                    };

                bool? passwordResult =
                    passwordWindow.ShowDialog();

                if (passwordResult != true)
                {
                    HintTextBlock.Text =
                        "尚未设置新密码，软件继续保持锁定。";

                    return;
                }

                MessageBox.Show(
                    "锁定密码已重新设置成功，请使用新密码解锁。",
                    "重置成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ForgotPasswordPanel.Visibility =
                    Visibility.Collapsed;

                ResetLicenseKeyTextBox.Clear();

                UnlockPasswordBox.Clear();

                UnlockPasswordBox.IsEnabled =
                    true;

                ConfirmButton.IsEnabled =
                    true;

                HintTextBlock.Text =
                    "请输入新设置的解锁密码。";

                UnlockPasswordBox.Focus();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "重置锁定密码失败：\n\n" +
                    exception.Message,
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private LockSettings? LoadLockSettings()
        {
            try
            {
                if (!File.Exists(
                        _lockSettingsPath))
                {
                    return null;
                }

                string json =
                    File.ReadAllText(
                        _lockSettingsPath,
                        Encoding.UTF8);

                return JsonSerializer
                    .Deserialize<LockSettings>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });
            }
            catch
            {
                return null;
            }
        }

        private void SaveLockSettings(
            LockSettings settings)
        {
            try
            {
                string? folder =
                    Path.GetDirectoryName(
                        _lockSettingsPath);

                if (!string.IsNullOrWhiteSpace(
                        folder))
                {
                    Directory.CreateDirectory(
                        folder);
                }

                string json =
                    JsonSerializer.Serialize(
                        settings,
                        new JsonSerializerOptions
                        {
                            WriteIndented =
                                true
                        });

                File.WriteAllText(
                    _lockSettingsPath,
                    json,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool VerifyPassword(
            string password,
            string savedPasswordHash)
        {
            try
            {
                byte[] passwordBytes =
                    Encoding.UTF8.GetBytes(
                        password);

                byte[] enteredHash =
                    SHA256.HashData(
                        passwordBytes);

                byte[] savedHash =
                    Convert.FromHexString(
                        savedPasswordHash);

                if (enteredHash.Length !=
                    savedHash.Length)
                {
                    return false;
                }

                return CryptographicOperations
                    .FixedTimeEquals(
                        enteredHash,
                        savedHash);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetLockoutRemaining(
            LockSettings settings,
            out TimeSpan remaining)
        {
            remaining =
                TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(
                    settings.LockoutUntil))
            {
                return false;
            }

            if (!DateTimeOffset.TryParse(
                    settings.LockoutUntil,
                    out DateTimeOffset lockoutUntil))
            {
                settings.FailedAttempts =
                    0;

                settings.LockoutUntil =
                    null;

                SaveLockSettings(
                    settings);

                return false;
            }

            remaining =
                lockoutUntil -
                DateTimeOffset.UtcNow;

            if (remaining >
                TimeSpan.Zero)
            {
                return true;
            }

            settings.FailedAttempts =
                0;

            settings.LockoutUntil =
                null;

            SaveLockSettings(
                settings);

            remaining =
                TimeSpan.Zero;

            return false;
        }

        private void CheckCurrentLockout()
        {
            LockSettings? settings =
                LoadLockSettings();

            if (settings == null)
            {
                HintTextBlock.Text =
                    "没有找到锁定密码，请点击“忘记密码”重新设置。";

                UnlockPasswordBox.IsEnabled =
                    false;

                ConfirmButton.IsEnabled =
                    false;

                return;
            }

            if (TryGetLockoutRemaining(
                    settings,
                    out TimeSpan remaining))
            {
                StartLockoutTimer();

                ShowLockoutStatus(
                    remaining);

                return;
            }

            UnlockPasswordBox.IsEnabled =
                true;

            ConfirmButton.IsEnabled =
                true;

            HintTextBlock.Text =
                "请输入解锁密码";
        }

        private void StartLockoutTimer()
        {
            if (!_lockoutTimer.IsEnabled)
            {
                _lockoutTimer.Start();
            }
        }

        private void StopLockoutTimer()
        {
            if (_lockoutTimer.IsEnabled)
            {
                _lockoutTimer.Stop();
            }
        }

        private void LockoutTimer_Tick(
            object? sender,
            EventArgs e)
        {
            LockSettings? settings =
                LoadLockSettings();

            if (settings == null ||
                !TryGetLockoutRemaining(
                    settings,
                    out TimeSpan remaining))
            {
                StopLockoutTimer();

                UnlockPasswordBox.IsEnabled =
                    true;

                ConfirmButton.IsEnabled =
                    true;

                HintTextBlock.Text =
                    "限制时间已结束，请重新输入密码。";

                UnlockPasswordBox.Focus();

                return;
            }

            ShowLockoutStatus(
                remaining);
        }

        private void ShowLockoutStatus(
            TimeSpan remaining)
        {
            UnlockPasswordBox.IsEnabled =
                false;

            ConfirmButton.IsEnabled =
                false;

            int totalSeconds =
                Math.Max(
                    0,
                    (int)Math.Ceiling(
                        remaining.TotalSeconds));

            int minutes =
                totalSeconds /
                60;

            int seconds =
                totalSeconds %
                60;

            HintTextBlock.Text =
                $"密码错误次数过多，请在 {minutes:00}:{seconds:00} 后再试。";
        }

        private static string? ReadSavedLicenseKey()
        {
            try
            {
                string licensePath =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "MultiChatManager2",
                        "license.json");

                if (!File.Exists(
                        licensePath))
                {
                    return null;
                }

                string json =
                    File.ReadAllText(
                        licensePath,
                        Encoding.UTF8);

                using JsonDocument document =
                    JsonDocument.Parse(
                        json);

                JsonElement root =
                    document.RootElement;

                if (root.TryGetProperty(
                        "LicenseKey",
                        out JsonElement licenseKeyElement) ||
                    root.TryGetProperty(
                        "licenseKey",
                        out licenseKeyElement))
                {
                    return licenseKeyElement
                        .GetString()?
                        .Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}