using MultiChatManager2.Updates;
using System.Windows;

namespace MultiChatManager2;

public partial class UpdateDialog :
    Window
{
    private readonly UpdateCoordinator _coordinator;
    private readonly CancellationTokenSource _cancellationTokenSource =
        new();

    private UpdateCheckResult? _checkResult;
    private PreparedUpdate? _preparedUpdate;
    private bool _operationRunning;

    public UpdateDialog(
        UpdateCoordinator coordinator)
    {
        InitializeComponent();

        _coordinator =
            coordinator;

        Loaded +=
            UpdateDialog_Loaded;

        Closing +=
            UpdateDialog_Closing;
    }

    private async void UpdateDialog_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        SetOperationState(
            true);

        try
        {
            _checkResult =
                await _coordinator.CheckAsync(
                    CreateProgress(),
                    _cancellationTokenSource.Token);

            if (!_checkResult.IsUpdateAvailable ||
                _checkResult.Manifest is null)
            {
                TitleText.Text =
                    "当前已是最新版本";

                VersionText.Text =
                    "当前版本：" +
                    _checkResult.CurrentVersion;

                ReleaseNotesText.Text =
                    _checkResult.Message;

                StatusText.Text =
                    string.Empty;

                UpdateProgressBar.IsIndeterminate =
                    false;

                UpdateProgressBar.Value =
                    100;

                InstallButton.IsEnabled =
                    false;

                CancelButton.Content =
                    "关闭";

                return;
            }

            TitleText.Text =
                _checkResult.IsMandatory
                    ? "发现必须安装的更新"
                    : "发现新版本";

            VersionText.Text =
                $"当前版本 {_checkResult.CurrentVersion}  →  最新版本 {_checkResult.LatestVersion}";

            ReleaseNotesText.Text =
                string.IsNullOrWhiteSpace(
                    _checkResult.Manifest.ReleaseNotes)
                    ? "本次更新未提供更新说明。"
                    : _checkResult.Manifest.ReleaseNotes;

            StatusText.Text =
                _checkResult.Message;

            UpdateProgressBar.IsIndeterminate =
                false;

            UpdateProgressBar.Value =
                0;

            InstallButton.IsEnabled =
                true;

            CancelButton.IsEnabled =
                !_checkResult.IsMandatory;
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception exception)
        {
            TitleText.Text =
                "检查更新失败";

            ReleaseNotesText.Text =
                exception.Message;

            StatusText.Text =
                string.Empty;

            UpdateProgressBar.IsIndeterminate =
                false;

            InstallButton.IsEnabled =
                false;

            CancelButton.Content =
                "关闭";
        }
        finally
        {
            SetOperationState(
                false,
                preserveInstallState: true);
        }
    }

    private async void InstallButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_checkResult?.Manifest is null)
        {
            return;
        }

        SetOperationState(
            true);

        try
        {
            _preparedUpdate =
                await _coordinator.PrepareAsync(
                    _checkResult.Manifest,
                    CreateProgress(),
                    _cancellationTokenSource.Token);

            StatusText.Text =
                "即将退出程序并安装更新……";

            _coordinator.LaunchUpdaterAndExit(
                _preparedUpdate,
                Environment.GetCommandLineArgs()
                    .Skip(1)
                    .ToArray(),
                beforeExit:
                    () =>
                    {
                        Dispatcher.Invoke(
                            () =>
                            {
                                Application.Current
                                    .ShutdownMode =
                                    ShutdownMode.OnExplicitShutdown;

                                Application.Current
                                    .Shutdown();
                            });
                    });
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "更新已取消。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetOperationState(
                false,
                preserveInstallState: true);
        }
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_operationRunning)
        {
            _cancellationTokenSource.Cancel();

            return;
        }

        Close();
    }

    private IProgress<UpdateProgressInfo>
        CreateProgress() =>
        new Progress<UpdateProgressInfo>(
            info =>
            {
                StatusText.Text =
                    info.Message;

                if (info.Percentage is double percentage)
                {
                    UpdateProgressBar.IsIndeterminate =
                        false;

                    UpdateProgressBar.Value =
                        percentage;
                }
                else
                {
                    UpdateProgressBar.IsIndeterminate =
                        info.Stage is
                            UpdateStage.Checking or
                            UpdateStage.Verifying or
                            UpdateStage.Preparing or
                            UpdateStage.Launching;
                }
            });

    private void SetOperationState(
        bool running,
        bool preserveInstallState = false)
    {
        _operationRunning =
            running;

        if (running)
        {
            InstallButton.IsEnabled =
                false;

            CancelButton.Content =
                "取消";
        }
        else
        {
            if (!preserveInstallState)
            {
                InstallButton.IsEnabled =
                    _checkResult?.IsUpdateAvailable ==
                    true;
            }
            else if (_checkResult?.IsUpdateAvailable ==
                     true)
            {
                InstallButton.IsEnabled =
                    true;
            }

            CancelButton.Content =
                "关闭";
        }
    }

    private void UpdateDialog_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (_operationRunning &&
            _checkResult?.IsMandatory == true)
        {
            e.Cancel = true;

            return;
        }

        _cancellationTokenSource.Cancel();
    }
}
