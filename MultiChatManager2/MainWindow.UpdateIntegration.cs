using MultiChatManager2.Updates;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace MultiChatManager2
{
    public partial class MainWindow
    {
        private UpdateCoordinator? _updateCoordinator;
        private bool _restartRequested;
        private void InitializeUpdateModule()
        {
            if (_updateCoordinator != null)
            {
                return;
            }

            string baseDirectory =
                AppContext.BaseDirectory;

            string currentVersion =
                GetCurrentApplicationVersion();

            string executablePath =
                Environment.ProcessPath
                ?? Path.Combine(
                    baseDirectory,
                    "LineTranslate.exe");

            UpdateOptions options =
                new UpdateOptions
                {
                    ManifestUri =
                        new Uri(
                            "https://iyadtuwabsmiohkyfvqv.supabase.co/storage/v1/object/public/updates/manifest.json"),

                    ProductId =
                        "LineTranslate",

                    Channel =
                        "stable",

                    CurrentVersion =
                        currentVersion,

                    InstallDirectory =
    baseDirectory,

                    MainExecutablePath =
                        executablePath,

                    UpdaterExecutablePath =
                        Path.Combine(
                            baseDirectory,
                            "Updater",
                            "LineTranslate.Updater.exe"),

                    WorkDirectory =
                        Path.Combine(
                            Environment.GetFolderPath(
                                Environment.SpecialFolder.LocalApplicationData),
                            "LcyLineTranslate",
                            "Updates"),

                    RsaPublicKeyPem =
                        null,

                    RequireManifestSignature =
                        false,

                    RequirePackageSignature =
                        false,

                    AllowPrerelease =
                        false,

                    AllowDowngrade =
                        false,

                    RequestTimeout =
                        TimeSpan.FromSeconds(30),

                    MaximumRetryCount =
                        4,

                    MaximumPackageBytes =
                        1024L * 1024L * 1024L
                };

            _updateCoordinator =
                new UpdateCoordinator(
                    options);

            ShowPreviousUpdateResult(
                options.WorkDirectory);
        }

        private static string GetCurrentApplicationVersion()
        {
            Version? version =
                Assembly
                    .GetExecutingAssembly()
                    .GetName()
                    .Version;

            if (version == null)
            {
                return "1.0.0";
            }

            int build =
                version.Build < 0
                    ? 0
                    : version.Build;

            return
                $"{version.Major}." +
                $"{version.Minor}." +
                $"{build}";
        }

        private void ShowPreviousUpdateResult(
            string workDirectory)
        {
            UpdateRecoveryResult recovery =
                UpdateRecovery.Inspect(
                    workDirectory);

            if (!recovery.HasResult)
            {
                return;
            }

            MessageBox.Show(
                this,
                recovery.Message,
                recovery.Success
                    ? "更新完成"
                    : "更新失败",
                MessageBoxButton.OK,
                recovery.Success
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);

            ClearPreviousUpdateResults(
                workDirectory);
        }

        private static void ClearPreviousUpdateResults(
            string workDirectory)
        {
            try
            {
                foreach (string resultPath in
                         Directory.EnumerateFiles(
                             workDirectory,
                             "apply-result.json",
                             SearchOption.AllDirectories))
                {
                    File.Delete(resultPath);
                }
            }
            catch
            {
                // 清理提示记录失败时，不影响程序正常启动。
            }
        }

        private void DisposeUpdateModule()
        {
            _updateCoordinator?.Dispose();

            _updateCoordinator =
                null;
        }

        private void UpdateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_updateCoordinator == null)
            {
                MessageBox.Show(
                    this,
                    "更新模块尚未初始化。",
                    "无法检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            UpdateDialog dialog =
                new UpdateDialog(
                    _updateCoordinator)
                {
                    Owner =
                        this
                };

            dialog.ShowDialog();
        }

        private void RestartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_restartRequested)
            {
                return;
            }

            string executablePath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "无法确定当前程序路径。");

            ProcessStartInfo startInfo =
                new()
                {
                    FileName = executablePath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true
                };

            foreach (string argument in
                     Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            _restartRequested = true;

            Application.Current.Exit +=
                (_, __) =>
                {
                    try
                    {
                        Process.Start(startInfo);
                    }
                    catch
                    {
                    }
                };

            Application.Current.Shutdown();
        }
    }
}
